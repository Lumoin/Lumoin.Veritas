using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Xml;

/// <summary>
/// Writes an <see cref="OwlOntologyDocument"/> as an OWL 2 XML serialization
/// document. Each axiom and expression renders directly to its element — the
/// serialization mirrors the structural specification one-to-one — with full
/// IRIs throughout, so the output re-reads to the same structural model.
/// </summary>
/// <remarks>
/// Nested class expressions and data ranges render over an explicit work-stack
/// so arbitrarily deep nesting never touches the call stack; bounded forms
/// (property expressions, terms, literals, facet restrictions) render inline.
/// The output is deterministic: one axiom per line, attributes in a fixed order,
/// and the same separators for the same shapes.
/// </remarks>
public static class OwlXmlSyntaxWriter
{
    /// <summary>The XML declaration and the open of the OWL namespace-bearing ontology element.</summary>
    private static ReadOnlySpan<byte> Header => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\""u8;

    /// <summary>The placeholder node a structurally-impossible term renders as, shared so the fallback allocates nothing.</summary>
    private static NamedNode InvalidNamed { get; } = new(new Utf8String("urn:veritas:invalid"u8.ToArray()));

    /// <summary>Writes an ontology document as OWL/XML to a buffer writer.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="output">The buffer writer the UTF-8 bytes are written to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static void Write(OwlOntologyDocument document, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(document);

        output.Write(Header);
        if(document.OntologyIri is NamedNode ontology)
        {
            output.Write(" ontologyIRI=\""u8);
            WriteAttributeValue(output, ontology.Iri);
            output.Write("\""u8);
        }

        output.Write(">\n"u8);

        foreach(OwlAxiom axiom in document.Axioms)
        {
            WriteAxiom(axiom, output);
            output.Write("\n"u8);
        }

        output.Write("</Ontology>\n"u8);
    }

    /// <summary>Writes an ontology document as an OWL/XML string.</summary>
    /// <param name="document">The document to write.</param>
    /// <returns>The OWL/XML serialization.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static string Write(OwlOntologyDocument document)
    {
        ArrayBufferWriter<byte> output = new();
        Write(document, output);

        return System.Text.Encoding.UTF8.GetString(output.WrittenSpan);
    }

    /// <summary>Writes one axiom: its element, its leading annotations, and its operands.</summary>
    /// <param name="axiom">The axiom to write.</param>
    /// <param name="output">The buffer writer.</param>
    private static void WriteAxiom(OwlAxiom axiom, IBufferWriter<byte> output)
    {
        switch(axiom)
        {
            case(OwlImportAxiom import):
            {
                output.Write("<Import>"u8);
                WriteText(output, import.Imported.Iri);
                output.Write("</Import>"u8);
                break;
            }
            case(OwlDeclarationAxiom declaration):
            {
                Open(output, OwlXmlElement.Declaration, axiom.Annotations);
                WriteEntity(output, EntityElement(declaration.Kind), declaration.Entity);
                Close(output, OwlXmlElement.Declaration);
                break;
            }
            case(OwlSubClassOfAxiom subClass):
            {
                Open(output, OwlXmlElement.SubClassOf, axiom.Annotations);
                Emit(subClass.SubClass, output);
                Emit(subClass.SuperClass, output);
                Close(output, OwlXmlElement.SubClassOf);
                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                Open(output, OwlXmlElement.EquivalentClasses, axiom.Annotations);
                Emit(equivalent.First, output);
                Emit(equivalent.Second, output);
                Close(output, OwlXmlElement.EquivalentClasses);
                break;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                Open(output, OwlXmlElement.DisjointClasses, axiom.Annotations);
                foreach(OwlClassExpression operand in disjoint.Operands)
                {
                    Emit(operand, output);
                }

                Close(output, OwlXmlElement.DisjointClasses);
                break;
            }
            case(OwlDisjointUnionAxiom union):
            {
                Open(output, OwlXmlElement.DisjointUnion, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.Class, union.Class);
                foreach(OwlClassExpression operand in union.Operands)
                {
                    Emit(operand, output);
                }

                Close(output, OwlXmlElement.DisjointUnion);
                break;
            }
            case(OwlSubObjectPropertyOfAxiom subProperty):
            {
                Open(output, OwlXmlElement.SubObjectPropertyOf, axiom.Annotations);
                WriteProperty(output, subProperty.SubProperty);
                WriteProperty(output, subProperty.SuperProperty);
                Close(output, OwlXmlElement.SubObjectPropertyOf);
                break;
            }
            case(OwlPropertyChainAxiom chain):
            {
                Open(output, OwlXmlElement.SubObjectPropertyOf, axiom.Annotations);
                output.Write("<ObjectPropertyChain>"u8);
                foreach(OwlObjectPropertyExpression link in chain.Chain)
                {
                    WriteProperty(output, link);
                }

                output.Write("</ObjectPropertyChain>"u8);
                WriteProperty(output, chain.SuperProperty);
                Close(output, OwlXmlElement.SubObjectPropertyOf);
                break;
            }
            case(OwlEquivalentObjectPropertiesAxiom equivalent):
            {
                Open(output, OwlXmlElement.EquivalentObjectProperties, axiom.Annotations);
                WriteProperty(output, equivalent.First);
                WriteProperty(output, equivalent.Second);
                Close(output, OwlXmlElement.EquivalentObjectProperties);
                break;
            }
            case(OwlDisjointObjectPropertiesAxiom disjoint):
            {
                Open(output, OwlXmlElement.DisjointObjectProperties, axiom.Annotations);
                foreach(OwlObjectPropertyExpression operand in disjoint.Operands)
                {
                    WriteProperty(output, operand);
                }

                Close(output, OwlXmlElement.DisjointObjectProperties);
                break;
            }
            case(OwlInverseObjectPropertiesAxiom inverse):
            {
                Open(output, OwlXmlElement.InverseObjectProperties, axiom.Annotations);
                WriteProperty(output, inverse.First);
                WriteProperty(output, inverse.Second);
                Close(output, OwlXmlElement.InverseObjectProperties);
                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                Open(output, OwlXmlElement.ObjectPropertyDomain, axiom.Annotations);
                WriteProperty(output, domain.Property);
                Emit(domain.Domain, output);
                Close(output, OwlXmlElement.ObjectPropertyDomain);
                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                Open(output, OwlXmlElement.ObjectPropertyRange, axiom.Annotations);
                WriteProperty(output, range.Property);
                Emit(range.Range, output);
                Close(output, OwlXmlElement.ObjectPropertyRange);
                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                OwlXmlElement element = CharacteristicElement(characteristic.Characteristic);
                Open(output, element, axiom.Annotations);
                WriteProperty(output, characteristic.Property);
                Close(output, element);
                break;
            }
            case(OwlSubDataPropertyOfAxiom subProperty):
            {
                Open(output, OwlXmlElement.SubDataPropertyOf, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.DataProperty, subProperty.SubProperty);
                WriteEntity(output, OwlXmlElement.DataProperty, subProperty.SuperProperty);
                Close(output, OwlXmlElement.SubDataPropertyOf);
                break;
            }
            case(OwlEquivalentDataPropertiesAxiom equivalent):
            {
                Open(output, OwlXmlElement.EquivalentDataProperties, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.DataProperty, equivalent.First);
                WriteEntity(output, OwlXmlElement.DataProperty, equivalent.Second);
                Close(output, OwlXmlElement.EquivalentDataProperties);
                break;
            }
            case(OwlDisjointDataPropertiesAxiom disjoint):
            {
                Open(output, OwlXmlElement.DisjointDataProperties, axiom.Annotations);
                foreach(NamedNode operand in disjoint.Operands)
                {
                    WriteEntity(output, OwlXmlElement.DataProperty, operand);
                }

                Close(output, OwlXmlElement.DisjointDataProperties);
                break;
            }
            case(OwlDataPropertyDomainAxiom domain):
            {
                Open(output, OwlXmlElement.DataPropertyDomain, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.DataProperty, domain.Property);
                Emit(domain.Domain, output);
                Close(output, OwlXmlElement.DataPropertyDomain);
                break;
            }
            case(OwlDataPropertyRangeAxiom range):
            {
                Open(output, OwlXmlElement.DataPropertyRange, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.DataProperty, range.Property);
                Emit(range.Range, output);
                Close(output, OwlXmlElement.DataPropertyRange);
                break;
            }
            case(OwlFunctionalDataPropertyAxiom functional):
            {
                Open(output, OwlXmlElement.FunctionalDataProperty, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.DataProperty, functional.Property);
                Close(output, OwlXmlElement.FunctionalDataProperty);
                break;
            }
            case(OwlDatatypeDefinitionAxiom definition):
            {
                Open(output, OwlXmlElement.DatatypeDefinition, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.Datatype, definition.Datatype);
                Emit(definition.Range, output);
                Close(output, OwlXmlElement.DatatypeDefinition);
                break;
            }
            case(OwlHasKeyAxiom key):
            {
                Open(output, OwlXmlElement.HasKey, axiom.Annotations);
                Emit(key.Class, output);
                foreach(OwlObjectPropertyExpression property in key.ObjectProperties)
                {
                    WriteProperty(output, property);
                }

                foreach(NamedNode property in key.DataProperties)
                {
                    WriteEntity(output, OwlXmlElement.DataProperty, property);
                }

                Close(output, OwlXmlElement.HasKey);
                break;
            }
            case(OwlSameIndividualAxiom same):
            {
                Open(output, OwlXmlElement.SameIndividual, axiom.Annotations);
                WriteIndividual(output, same.First);
                WriteIndividual(output, same.Second);
                Close(output, OwlXmlElement.SameIndividual);
                break;
            }
            case(OwlDifferentIndividualsAxiom different):
            {
                Open(output, OwlXmlElement.DifferentIndividuals, axiom.Annotations);
                foreach(RdfTerm individual in different.Individuals)
                {
                    WriteIndividual(output, individual);
                }

                Close(output, OwlXmlElement.DifferentIndividuals);
                break;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                Open(output, OwlXmlElement.ClassAssertion, axiom.Annotations);
                Emit(assertion.Class, output);
                WriteIndividual(output, assertion.Individual);
                Close(output, OwlXmlElement.ClassAssertion);
                break;
            }
            case(OwlObjectPropertyAssertionAxiom assertion):
            {
                Open(output, OwlXmlElement.ObjectPropertyAssertion, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.ObjectProperty, assertion.Property);
                WriteIndividual(output, assertion.Source);
                WriteIndividual(output, assertion.Target);
                Close(output, OwlXmlElement.ObjectPropertyAssertion);
                break;
            }
            case(OwlNegativeObjectPropertyAssertionAxiom assertion):
            {
                Open(output, OwlXmlElement.NegativeObjectPropertyAssertion, axiom.Annotations);
                WriteProperty(output, assertion.Property);
                WriteIndividual(output, assertion.Source);
                WriteIndividual(output, assertion.Target);
                Close(output, OwlXmlElement.NegativeObjectPropertyAssertion);
                break;
            }
            case(OwlDataPropertyAssertionAxiom assertion):
            {
                Open(output, OwlXmlElement.DataPropertyAssertion, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.DataProperty, assertion.Property);
                WriteIndividual(output, assertion.Source);
                WriteLiteral(output, assertion.Target);
                Close(output, OwlXmlElement.DataPropertyAssertion);
                break;
            }
            case(OwlNegativeDataPropertyAssertionAxiom assertion):
            {
                Open(output, OwlXmlElement.NegativeDataPropertyAssertion, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.DataProperty, assertion.Property);
                WriteIndividual(output, assertion.Source);
                WriteLiteral(output, assertion.Target);
                Close(output, OwlXmlElement.NegativeDataPropertyAssertion);
                break;
            }
            case(OwlAnnotationAssertionAxiom assertion):
            {
                Open(output, OwlXmlElement.AnnotationAssertion, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.AnnotationProperty, assertion.Property);
                WriteAnnotationTerm(output, assertion.Subject);
                WriteAnnotationTerm(output, assertion.Value);
                Close(output, OwlXmlElement.AnnotationAssertion);
                break;
            }
            case(OwlSubAnnotationPropertyOfAxiom subProperty):
            {
                Open(output, OwlXmlElement.SubAnnotationPropertyOf, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.AnnotationProperty, subProperty.SubProperty);
                WriteEntity(output, OwlXmlElement.AnnotationProperty, subProperty.SuperProperty);
                Close(output, OwlXmlElement.SubAnnotationPropertyOf);
                break;
            }
            case(OwlAnnotationPropertyDomainAxiom domain):
            {
                Open(output, OwlXmlElement.AnnotationPropertyDomain, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.AnnotationProperty, domain.Property);
                WriteIri(output, domain.Domain);
                Close(output, OwlXmlElement.AnnotationPropertyDomain);
                break;
            }
            case(OwlAnnotationPropertyRangeAxiom range):
            {
                Open(output, OwlXmlElement.AnnotationPropertyRange, axiom.Annotations);
                WriteEntity(output, OwlXmlElement.AnnotationProperty, range.Property);
                WriteIri(output, range.Range);
                Close(output, OwlXmlElement.AnnotationPropertyRange);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Renders a class expression or data range and its full subtree over an explicit work-stack.</summary>
    /// <param name="root">The expression or data-range root.</param>
    /// <param name="output">The buffer writer.</param>
    private static void Emit(object root, IBufferWriter<byte> output)
    {
        Stack<Step> work = new();
        work.Push(new Step(OwlXmlElement.Unknown, root));
        while(work.Count > 0)
        {
            Step step = work.Pop();
            if(step.Node is null)
            {
                Close(output, step.Close);

                continue;
            }

            Expand(step.Node, output, work);
        }
    }

    /// <summary>Renders the open of a node and pushes its close and nesting children.</summary>
    /// <param name="node">The node to expand.</param>
    /// <param name="output">The buffer writer.</param>
    /// <param name="work">The work-stack.</param>
    private static void Expand(object node, IBufferWriter<byte> output, Stack<Step> work)
    {
        switch(node)
        {
            case(OwlClassReference reference):
            {
                WriteEntity(output, OwlXmlElement.Class, reference.Class);
                break;
            }
            case(OwlObjectIntersectionOf intersection):
            {
                EmitGroup(output, work, OwlXmlElement.ObjectIntersectionOf, intersection.Operands);
                break;
            }
            case(OwlObjectUnionOf union):
            {
                EmitGroup(output, work, OwlXmlElement.ObjectUnionOf, union.Operands);
                break;
            }
            case(OwlObjectComplementOf complement):
            {
                WriteOpen(output, OwlXmlElement.ObjectComplementOf);
                Push(work, OwlXmlElement.ObjectComplementOf, complement.Operand);
                break;
            }
            case(OwlObjectOneOf enumeration):
            {
                WriteOpen(output, OwlXmlElement.ObjectOneOf);
                foreach(RdfTerm individual in enumeration.Individuals)
                {
                    WriteIndividual(output, individual);
                }

                Close(output, OwlXmlElement.ObjectOneOf);
                break;
            }
            case(OwlObjectSomeValuesFrom some):
            {
                WriteOpen(output, OwlXmlElement.ObjectSomeValuesFrom);
                WriteProperty(output, some.Property);
                Push(work, OwlXmlElement.ObjectSomeValuesFrom, some.Filler);
                break;
            }
            case(OwlObjectAllValuesFrom all):
            {
                WriteOpen(output, OwlXmlElement.ObjectAllValuesFrom);
                WriteProperty(output, all.Property);
                Push(work, OwlXmlElement.ObjectAllValuesFrom, all.Filler);
                break;
            }
            case(OwlObjectHasValue hasValue):
            {
                WriteOpen(output, OwlXmlElement.ObjectHasValue);
                WriteProperty(output, hasValue.Property);
                WriteIndividual(output, hasValue.Individual);
                Close(output, OwlXmlElement.ObjectHasValue);
                break;
            }
            case(OwlObjectHasSelf hasSelf):
            {
                WriteOpen(output, OwlXmlElement.ObjectHasSelf);
                WriteProperty(output, hasSelf.Property);
                Close(output, OwlXmlElement.ObjectHasSelf);
                break;
            }
            case(OwlObjectCardinality cardinality):
            {
                OwlXmlElement element = ObjectCardinalityElement(cardinality.Kind);
                WriteCardinalityOpen(output, element, cardinality.Cardinality);
                WriteProperty(output, cardinality.Property);
                if(cardinality.Filler is OwlClassExpression filler)
                {
                    Push(work, element, filler);
                }
                else
                {
                    Close(output, element);
                }

                break;
            }
            case(OwlDataSomeValuesFrom some):
            {
                WriteOpen(output, OwlXmlElement.DataSomeValuesFrom);
                foreach(NamedNode property in some.Properties)
                {
                    WriteEntity(output, OwlXmlElement.DataProperty, property);
                }

                Push(work, OwlXmlElement.DataSomeValuesFrom, some.Range);
                break;
            }
            case(OwlDataAllValuesFrom all):
            {
                WriteOpen(output, OwlXmlElement.DataAllValuesFrom);
                foreach(NamedNode property in all.Properties)
                {
                    WriteEntity(output, OwlXmlElement.DataProperty, property);
                }

                Push(work, OwlXmlElement.DataAllValuesFrom, all.Range);
                break;
            }
            case(OwlDataHasValue hasValue):
            {
                WriteOpen(output, OwlXmlElement.DataHasValue);
                WriteEntity(output, OwlXmlElement.DataProperty, hasValue.Property);
                WriteLiteral(output, hasValue.Value);
                Close(output, OwlXmlElement.DataHasValue);
                break;
            }
            case(OwlDataCardinality cardinality):
            {
                OwlXmlElement element = DataCardinalityElement(cardinality.Kind);
                WriteCardinalityOpen(output, element, cardinality.Cardinality);
                WriteEntity(output, OwlXmlElement.DataProperty, cardinality.Property);
                if(cardinality.Range is OwlDataRange range)
                {
                    Push(work, element, range);
                }
                else
                {
                    Close(output, element);
                }

                break;
            }
            case(OwlDatatypeReference reference):
            {
                WriteEntity(output, OwlXmlElement.Datatype, reference.Datatype);
                break;
            }
            case(OwlDataIntersectionOf intersection):
            {
                EmitRangeGroup(output, work, OwlXmlElement.DataIntersectionOf, intersection.Ranges);
                break;
            }
            case(OwlDataUnionOf union):
            {
                EmitRangeGroup(output, work, OwlXmlElement.DataUnionOf, union.Ranges);
                break;
            }
            case(OwlDataComplementOf complement):
            {
                WriteOpen(output, OwlXmlElement.DataComplementOf);
                Push(work, OwlXmlElement.DataComplementOf, complement.Range);
                break;
            }
            case(OwlDataOneOf enumeration):
            {
                WriteOpen(output, OwlXmlElement.DataOneOf);
                foreach(Literal literal in enumeration.Literals)
                {
                    WriteLiteral(output, literal);
                }

                Close(output, OwlXmlElement.DataOneOf);
                break;
            }
            case(OwlDatatypeRestriction restriction):
            {
                WriteOpen(output, OwlXmlElement.DatatypeRestriction);
                WriteEntity(output, OwlXmlElement.Datatype, restriction.Datatype);
                foreach(OwlFacetRestriction facet in restriction.Restrictions)
                {
                    WriteFacet(output, facet);
                }

                Close(output, OwlXmlElement.DatatypeRestriction);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Renders an n-ary class-expression group: its open, its operands, then its close.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="work">The work-stack.</param>
    /// <param name="element">The group element.</param>
    /// <param name="operands">The class-expression operands.</param>
    private static void EmitGroup(IBufferWriter<byte> output, Stack<Step> work, OwlXmlElement element, IReadOnlyList<OwlClassExpression> operands)
    {
        WriteOpen(output, element);
        work.Push(new Step(element, null));
        for(int i = operands.Count - 1; i >= 0; i--)
        {
            work.Push(new Step(OwlXmlElement.Unknown, operands[i]));
        }
    }

    /// <summary>Renders an n-ary data-range group: its open, its operands, then its close.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="work">The work-stack.</param>
    /// <param name="element">The group element.</param>
    /// <param name="ranges">The data-range operands.</param>
    private static void EmitRangeGroup(IBufferWriter<byte> output, Stack<Step> work, OwlXmlElement element, IReadOnlyList<OwlDataRange> ranges)
    {
        WriteOpen(output, element);
        work.Push(new Step(element, null));
        for(int i = ranges.Count - 1; i >= 0; i--)
        {
            work.Push(new Step(OwlXmlElement.Unknown, ranges[i]));
        }
    }

    /// <summary>Pushes the close of a single-child element and then the child, so the child renders before the close.</summary>
    /// <param name="work">The work-stack.</param>
    /// <param name="element">The element to close.</param>
    /// <param name="child">The child node.</param>
    private static void Push(Stack<Step> work, OwlXmlElement element, object child)
    {
        work.Push(new Step(element, null));
        work.Push(new Step(OwlXmlElement.Unknown, child));
    }

    /// <summary>Writes an element open tag and its leading annotations.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="element">The element.</param>
    /// <param name="annotations">The element's annotations.</param>
    private static void Open(IBufferWriter<byte> output, OwlXmlElement element, System.Collections.Immutable.ImmutableArray<OwlAnnotation> annotations)
    {
        WriteOpen(output, element);
        foreach(OwlAnnotation annotation in annotations)
        {
            EmitAnnotation(annotation, output);
        }
    }

    /// <summary>Renders an annotation and its nested annotations over an explicit work-stack.</summary>
    /// <param name="root">The annotation.</param>
    /// <param name="output">The buffer writer.</param>
    private static void EmitAnnotation(OwlAnnotation root, IBufferWriter<byte> output)
    {
        Stack<AnnotationStep> work = new();
        work.Push(new AnnotationStep(root, null));
        while(work.Count > 0)
        {
            AnnotationStep step = work.Pop();
            if(step.Suffix is OwlAnnotation suffix)
            {
                WriteEntity(output, OwlXmlElement.AnnotationProperty, suffix.Property);
                WriteAnnotationTerm(output, suffix.Value);
                Close(output, OwlXmlElement.Annotation);

                continue;
            }

            if(step.Emit is OwlAnnotation annotation)
            {
                WriteOpen(output, OwlXmlElement.Annotation);
                work.Push(new AnnotationStep(null, annotation));
                for(int i = annotation.Annotations.Length - 1; i >= 0; i--)
                {
                    work.Push(new AnnotationStep(annotation.Annotations[i], null));
                }
            }
        }
    }

    /// <summary>Writes an object property expression inline: a named property, or the inverse of one.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="property">The property expression.</param>
    private static void WriteProperty(IBufferWriter<byte> output, OwlObjectPropertyExpression property)
    {
        if(property.IsInverse)
        {
            output.Write("<ObjectInverseOf>"u8);
            WriteEntity(output, OwlXmlElement.ObjectProperty, property.Property);
            output.Write("</ObjectInverseOf>"u8);

            return;
        }

        WriteEntity(output, OwlXmlElement.ObjectProperty, property.Property);
    }

    /// <summary>Writes an individual term: a named individual, or an anonymous one.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="term">The individual term.</param>
    private static void WriteIndividual(IBufferWriter<byte> output, RdfTerm term)
    {
        if(term is BlankNode blank)
        {
            output.Write("<AnonymousIndividual nodeID=\""u8);
            WriteAttributeValue(output, blank.Label);
            output.Write("\"/>"u8);

            return;
        }

        if(term is NamedNode named)
        {
            WriteEntity(output, OwlXmlElement.NamedIndividual, named);

            return;
        }

        WriteEntity(output, OwlXmlElement.NamedIndividual, InvalidNamed);
    }

    /// <summary>Writes an annotation subject or value term: an IRI, an anonymous individual, or a literal.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="term">The annotation term.</param>
    private static void WriteAnnotationTerm(IBufferWriter<byte> output, RdfTerm term)
    {
        switch(term)
        {
            case(NamedNode named):
            {
                WriteIri(output, named);
                break;
            }
            case(BlankNode blank):
            {
                output.Write("<AnonymousIndividual nodeID=\""u8);
                WriteAttributeValue(output, blank.Label);
                output.Write("\"/>"u8);
                break;
            }
            case(Literal literal):
            {
                WriteLiteral(output, literal);
                break;
            }
            default:
            {
                WriteIri(output, InvalidNamed);
                break;
            }
        }
    }

    /// <summary>Writes a literal: its language tag or datatype, then its lexical value.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="literal">The literal.</param>
    private static void WriteLiteral(IBufferWriter<byte> output, Literal literal)
    {
        if(literal.Language is Utf8String language)
        {
            output.Write("<Literal xml:lang=\""u8);
            WriteAttributeValue(output, language);
            output.Write("\">"u8);
        }
        else
        {
            output.Write("<Literal datatypeIRI=\""u8);
            WriteAttributeValue(output, literal.Datatype.Iri);
            output.Write("\">"u8);
        }

        WriteText(output, literal.Value);
        output.Write("</Literal>"u8);
    }

    /// <summary>Writes a facet restriction: its facet IRI and its value literal.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="facet">The facet restriction.</param>
    private static void WriteFacet(IBufferWriter<byte> output, OwlFacetRestriction facet)
    {
        output.Write("<FacetRestriction facet=\""u8);
        WriteAttributeValue(output, facet.Facet.Iri);
        output.Write("\">"u8);
        WriteLiteral(output, facet.Value);
        output.Write("</FacetRestriction>"u8);
    }

    /// <summary>Writes an entity element with a full IRI: <c>&lt;Name IRI="…"/&gt;</c>.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="element">The entity element.</param>
    /// <param name="entity">The entity IRI node.</param>
    private static void WriteEntity(IBufferWriter<byte> output, OwlXmlElement element, NamedNode entity)
    {
        output.Write("<"u8);
        output.Write(OwlXmlNames.Name(element));
        output.Write(" IRI=\""u8);
        WriteAttributeValue(output, entity.Iri);
        output.Write("\"/>"u8);
    }

    /// <summary>Writes an IRI as the child-element form: <c>&lt;IRI&gt;…&lt;/IRI&gt;</c>.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="iri">The IRI node.</param>
    private static void WriteIri(IBufferWriter<byte> output, NamedNode iri)
    {
        output.Write("<IRI>"u8);
        WriteText(output, iri.Iri);
        output.Write("</IRI>"u8);
    }

    /// <summary>Writes an element open tag with no attributes: <c>&lt;Name&gt;</c>.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="element">The element.</param>
    private static void WriteOpen(IBufferWriter<byte> output, OwlXmlElement element)
    {
        output.Write("<"u8);
        output.Write(OwlXmlNames.Name(element));
        output.Write(">"u8);
    }

    /// <summary>Writes a cardinality element open tag: <c>&lt;Name cardinality="n"&gt;</c>.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="element">The cardinality element.</param>
    /// <param name="cardinality">The non-negative bound.</param>
    private static void WriteCardinalityOpen(IBufferWriter<byte> output, OwlXmlElement element, int cardinality)
    {
        output.Write("<"u8);
        output.Write(OwlXmlNames.Name(element));
        output.Write(" cardinality=\""u8);
        Span<byte> digits = stackalloc byte[16];
        Utf8Formatter.TryFormat(cardinality, digits, out int written);
        output.Write(digits.Slice(0, written));
        output.Write("\">"u8);
    }

    /// <summary>Writes an element close tag: <c>&lt;/Name&gt;</c>.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="element">The element.</param>
    private static void Close(IBufferWriter<byte> output, OwlXmlElement element)
    {
        output.Write("</"u8);
        output.Write(OwlXmlNames.Name(element));
        output.Write(">"u8);
    }

    /// <summary>Writes an attribute value, escaping the markup-significant bytes.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="value">The value to escape.</param>
    private static void WriteAttributeValue(IBufferWriter<byte> output, Utf8String value)
    {
        ReadOnlySpan<byte> bytes = value.Span;
        int start = 0;
        for(int i = 0; i < bytes.Length; i++)
        {
            ReadOnlySpan<byte> entity = AttributeEscape(bytes[i]);
            if(!entity.IsEmpty)
            {
                output.Write(bytes.Slice(start, i - start));
                output.Write(entity);
                start = i + 1;
            }
        }

        output.Write(bytes.Slice(start));
    }

    /// <summary>Writes element text, escaping the markup-significant bytes.</summary>
    /// <param name="output">The buffer writer.</param>
    /// <param name="value">The text to escape.</param>
    private static void WriteText(IBufferWriter<byte> output, Utf8String value)
    {
        ReadOnlySpan<byte> bytes = value.Span;
        int start = 0;
        for(int i = 0; i < bytes.Length; i++)
        {
            ReadOnlySpan<byte> entity = TextEscape(bytes[i]);
            if(!entity.IsEmpty)
            {
                output.Write(bytes.Slice(start, i - start));
                output.Write(entity);
                start = i + 1;
            }
        }

        output.Write(bytes.Slice(start));
    }

    /// <summary>The entity replacement for a byte inside an attribute value, or an empty span when the byte is written as-is.</summary>
    /// <param name="b">The byte.</param>
    /// <returns>The replacement entity, or an empty span.</returns>
    private static ReadOnlySpan<byte> AttributeEscape(byte b)
    {
        return b switch
        {
            (byte)'&' => "&amp;"u8,
            (byte)'<' => "&lt;"u8,
            (byte)'>' => "&gt;"u8,
            (byte)'"' => "&quot;"u8,
            _ => default,
        };
    }

    /// <summary>The entity replacement for a byte inside element text, or an empty span when the byte is written as-is.</summary>
    /// <param name="b">The byte.</param>
    /// <returns>The replacement entity, or an empty span.</returns>
    private static ReadOnlySpan<byte> TextEscape(byte b)
    {
        return b switch
        {
            (byte)'&' => "&amp;"u8,
            (byte)'<' => "&lt;"u8,
            (byte)'>' => "&gt;"u8,
            _ => default,
        };
    }

    /// <summary>The entity-element name for a declared entity kind.</summary>
    /// <param name="kind">The entity kind.</param>
    /// <returns>The entity element.</returns>
    private static OwlXmlElement EntityElement(OwlEntityKind kind)
    {
        return kind switch
        {
            OwlEntityKind.Class => OwlXmlElement.Class,
            OwlEntityKind.Datatype => OwlXmlElement.Datatype,
            OwlEntityKind.ObjectProperty => OwlXmlElement.ObjectProperty,
            OwlEntityKind.DataProperty => OwlXmlElement.DataProperty,
            OwlEntityKind.AnnotationProperty => OwlXmlElement.AnnotationProperty,
            _ => OwlXmlElement.NamedIndividual,
        };
    }

    /// <summary>The axiom element for an object-property characteristic.</summary>
    /// <param name="characteristic">The characteristic.</param>
    /// <returns>The characteristic axiom element.</returns>
    private static OwlXmlElement CharacteristicElement(OwlPropertyCharacteristic characteristic)
    {
        return characteristic switch
        {
            OwlPropertyCharacteristic.Functional => OwlXmlElement.FunctionalObjectProperty,
            OwlPropertyCharacteristic.InverseFunctional => OwlXmlElement.InverseFunctionalObjectProperty,
            OwlPropertyCharacteristic.Transitive => OwlXmlElement.TransitiveObjectProperty,
            OwlPropertyCharacteristic.Symmetric => OwlXmlElement.SymmetricObjectProperty,
            OwlPropertyCharacteristic.Asymmetric => OwlXmlElement.AsymmetricObjectProperty,
            OwlPropertyCharacteristic.Reflexive => OwlXmlElement.ReflexiveObjectProperty,
            _ => OwlXmlElement.IrreflexiveObjectProperty,
        };
    }

    /// <summary>The object cardinality element for a cardinality flavour.</summary>
    /// <param name="kind">The cardinality flavour.</param>
    /// <returns>The object cardinality element.</returns>
    private static OwlXmlElement ObjectCardinalityElement(OwlCardinalityKind kind)
    {
        return kind switch
        {
            OwlCardinalityKind.Min => OwlXmlElement.ObjectMinCardinality,
            OwlCardinalityKind.Max => OwlXmlElement.ObjectMaxCardinality,
            _ => OwlXmlElement.ObjectExactCardinality,
        };
    }

    /// <summary>The data cardinality element for a cardinality flavour.</summary>
    /// <param name="kind">The cardinality flavour.</param>
    /// <returns>The data cardinality element.</returns>
    private static OwlXmlElement DataCardinalityElement(OwlCardinalityKind kind)
    {
        return kind switch
        {
            OwlCardinalityKind.Min => OwlXmlElement.DataMinCardinality,
            OwlCardinalityKind.Max => OwlXmlElement.DataMaxCardinality,
            _ => OwlXmlElement.DataExactCardinality,
        };
    }

    /// <summary>One step on the expression render work-stack: a node to expand, or a close tag to write.</summary>
    /// <param name="Close">The element to close when <see cref="Node"/> is <see langword="null"/>.</param>
    /// <param name="Node">The class expression or data range to expand, or <see langword="null"/> for a close step.</param>
    private readonly record struct Step(OwlXmlElement Close, object? Node);

    /// <summary>One step on the annotation render work-stack: an annotation to expand, or one whose property and value close it.</summary>
    /// <param name="Emit">The annotation to expand, or <see langword="null"/> for a suffix step.</param>
    /// <param name="Suffix">The annotation whose property, value, and close tag finish it, or <see langword="null"/> for an expand step.</param>
    private readonly record struct AnnotationStep(OwlAnnotation? Emit, OwlAnnotation? Suffix);
}
