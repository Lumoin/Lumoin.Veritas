using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// Writes a structural document in OWL 2 functional syntax — the reverse of
/// <see cref="OwlFunctionalSyntaxReader"/>, completing the text round-trip:
/// what the reader produces, the writer renders back so that reading the
/// rendering reproduces the document.
/// </summary>
/// <remarks>
/// <para>
/// The rendering is UTF-8 byte-native and deterministic and prefix-free: every
/// IRI is written in full angle-bracket form, one axiom per line in document
/// order, axiom annotations leading their frame. Reading a rendering and
/// rendering the result reproduces the same text — the writer's output is its
/// own fixed point.
/// </para>
/// <para>
/// Keywords and punctuation are emitted directly from their <c>u8</c> byte
/// sequences and term values from their <see cref="Utf8String"/> bytes, so no
/// UTF-16 intermediate is ever materialised. Expression trees render through an
/// explicit work stack; the no-recursion discipline holds.
/// </para>
/// </remarks>
public static class OwlFunctionalSyntaxWriter
{
    /// <summary>
    /// Renders the document as UTF-8 functional-syntax bytes into a caller-supplied writer.
    /// </summary>
    /// <param name="document">The structural document.</param>
    /// <param name="output">The destination buffer writer the caller owns and threads.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    public static void Write(OwlOntologyDocument document, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        output.Write(OwlFunctionalKeywords.Ontology);
        output.Write("("u8);
        if(document.OntologyIri is NamedNode ontologyIri)
        {
            output.Write(" <"u8);
            output.Write(ontologyIri.Iri.Span);
            output.Write(">"u8);
        }

        output.Write("\n"u8);

        foreach(OwlAxiom axiom in document.Axioms)
        {
            output.Write("  "u8);
            Render(axiom, output);
            output.Write("\n"u8);
        }

        output.Write(")\n"u8);
    }

    /// <summary>
    /// Renders the document to functional-syntax text, decoding the UTF-8 bytes once at the boundary.
    /// </summary>
    /// <param name="document">The structural document.</param>
    /// <returns>The functional-syntax text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static string Write(OwlOntologyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ArrayBufferWriter<byte> buffer = new();
        Write(document, buffer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The lexical class of one work-stack step.</summary>
    private enum StepKind
    {
        /// <summary>Expand the carried node into its rendering.</summary>
        Node,

        /// <summary>Write a single separating space.</summary>
        Space,

        /// <summary>Close the current frame with <c>" )"</c>.</summary>
        Close,

        /// <summary>Write the carried boxed integer as its decimal digits.</summary>
        Number
    }

    /// <summary>One unit of rendering work: a node to expand, a structural marker, or a number.</summary>
    /// <param name="Kind">The step's lexical class.</param>
    /// <param name="Node">The node to expand or the boxed number, or <see langword="null"/> for a marker.</param>
    private readonly record struct Step(StepKind Kind, object? Node);

    //A nested constructor frame whose head is a declared entity kind, rendered
    //as Kind( <iri> ).
    private sealed record EntityDeclaration(OwlEntityKind Kind, Utf8String Iri);

    //A headed ObjectPropertyChain( … ) group nested inside a sub-property axiom.
    private sealed record PropertyChainGroup(List<object> Members);

    //A bare ( … ) group, as a HasKey axiom's object- and data-property lists.
    private sealed record BareGroup(List<object> Members);

    /// <summary>Renders one node through an explicit work stack into the output.</summary>
    /// <param name="root">The node to render.</param>
    /// <param name="output">The destination buffer writer.</param>
    private static void Render(object root, IBufferWriter<byte> output)
    {
        Stack<Step> work = new();
        work.Push(new Step(StepKind.Node, root));

        while(work.Count > 0)
        {
            Step step = work.Pop();
            switch(step.Kind)
            {
                case(StepKind.Space):
                {
                    output.Write(" "u8);
                    break;
                }
                case(StepKind.Close):
                {
                    output.Write(" )"u8);
                    break;
                }
                case(StepKind.Number):
                {
                    WriteNumber((int)step.Node!, output);
                    break;
                }
                default:
                {
                    ExpandNode(step.Node!, work, output);
                    break;
                }
            }
        }
    }

    //Expands one node: writes its leaf bytes directly, or writes its head and
    //pushes its frame's items, each preceded by a separating space and closed
    //by " )", so they pop in document order.
    private static void ExpandNode(object node, Stack<Step> work, IBufferWriter<byte> output)
    {
        switch(node)
        {
            case(NamedNode named):
            {
                output.Write("<"u8);
                output.Write(named.Iri.Span);
                output.Write(">"u8);
                break;
            }
            case(BlankNode blank):
            {
                output.Write("_:"u8);
                output.Write(blank.Label.Span);
                break;
            }
            case(Literal value):
            {
                WriteLiteral(value, output);
                break;
            }
            case(OwlAnnotation annotation):
            {
                PushFrame(work, output, OwlFunctionalKeywords.Annotation, [.. annotation.Annotations, annotation.Property, annotation.Value]);
                break;
            }
            case(OwlObjectPropertyReference property):
            {
                output.Write("<"u8);
                output.Write(property.Named.Iri.Span);
                output.Write(">"u8);
                break;
            }
            case(OwlInverseObjectProperty inverse):
            {
                output.Write(OwlFunctionalKeywords.ObjectInverseOf);
                output.Write("( <"u8);
                output.Write(inverse.Inverted.Iri.Span);
                output.Write("> )"u8);
                break;
            }
            case(EntityDeclaration entity):
            {
                PushFrame(work, output, DeclarationKind(entity.Kind), [new NamedNode(entity.Iri)]);
                break;
            }
            case(PropertyChainGroup chain):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectPropertyChain, chain.Members);
                break;
            }
            case(BareGroup bare):
            {
                PushFrame(work, output, default, bare.Members);
                break;
            }
            case(OwlClassExpression expression):
            {
                ExpandClassExpression(expression, work, output);
                break;
            }
            case(OwlDataRange range):
            {
                ExpandDataRange(range, work, output);
                break;
            }
            case(OwlAxiom axiom):
            {
                ExpandAxiom(axiom, work, output);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Expands a class expression: a leaf reference, or a headed frame.</summary>
    /// <param name="expression">The class expression.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="output">The destination buffer writer.</param>
    private static void ExpandClassExpression(OwlClassExpression expression, Stack<Step> work, IBufferWriter<byte> output)
    {
        switch(expression)
        {
            case(OwlClassReference reference):
            {
                output.Write("<"u8);
                output.Write(reference.Class.Iri.Span);
                output.Write(">"u8);
                break;
            }
            case(OwlObjectIntersectionOf intersection):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectIntersectionOf, [.. intersection.Operands]);
                break;
            }
            case(OwlObjectUnionOf union):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectUnionOf, [.. union.Operands]);
                break;
            }
            case(OwlObjectComplementOf complement):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectComplementOf, [complement.Operand]);
                break;
            }
            case(OwlObjectOneOf oneOf):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectOneOf, [.. oneOf.Individuals]);
                break;
            }
            case(OwlObjectSomeValuesFrom someValues):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectSomeValuesFrom, [someValues.Property, someValues.Filler]);
                break;
            }
            case(OwlObjectAllValuesFrom allValues):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectAllValuesFrom, [allValues.Property, allValues.Filler]);
                break;
            }
            case(OwlObjectHasValue hasValue):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectHasValue, [hasValue.Property, hasValue.Individual]);
                break;
            }
            case(OwlObjectHasSelf hasSelf):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectHasSelf, [hasSelf.Property]);
                break;
            }
            case(OwlObjectCardinality cardinality):
            {
                ReadOnlySpan<byte> head = cardinality.Kind switch
                {
                    OwlCardinalityKind.Min => OwlFunctionalKeywords.ObjectMinCardinality,
                    OwlCardinalityKind.Max => OwlFunctionalKeywords.ObjectMaxCardinality,
                    _ => OwlFunctionalKeywords.ObjectExactCardinality
                };

                List<object> items = [cardinality.Cardinality, cardinality.Property];
                if(cardinality.Filler is OwlClassExpression filler)
                {
                    items.Add(filler);
                }

                PushFrame(work, output, head, items);
                break;
            }
            case(OwlDataSomeValuesFrom dataSome):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataSomeValuesFrom, [.. dataSome.Properties, dataSome.Range]);
                break;
            }
            case(OwlDataAllValuesFrom dataAll):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataAllValuesFrom, [.. dataAll.Properties, dataAll.Range]);
                break;
            }
            case(OwlDataHasValue dataHasValue):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataHasValue, [dataHasValue.Property, dataHasValue.Value]);
                break;
            }
            case(OwlDataCardinality dataCardinality):
            {
                ReadOnlySpan<byte> head = dataCardinality.Kind switch
                {
                    OwlCardinalityKind.Min => OwlFunctionalKeywords.DataMinCardinality,
                    OwlCardinalityKind.Max => OwlFunctionalKeywords.DataMaxCardinality,
                    _ => OwlFunctionalKeywords.DataExactCardinality
                };

                List<object> items = [dataCardinality.Cardinality, dataCardinality.Property];
                if(dataCardinality.Range is OwlDataRange qualifier)
                {
                    items.Add(qualifier);
                }

                PushFrame(work, output, head, items);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Expands a data range: a leaf reference, or a headed frame.</summary>
    /// <param name="range">The data range.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="output">The destination buffer writer.</param>
    private static void ExpandDataRange(OwlDataRange range, Stack<Step> work, IBufferWriter<byte> output)
    {
        switch(range)
        {
            case(OwlDatatypeReference reference):
            {
                output.Write("<"u8);
                output.Write(reference.Datatype.Iri.Span);
                output.Write(">"u8);
                break;
            }
            case(OwlDataIntersectionOf intersection):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataIntersectionOf, [.. intersection.Ranges]);
                break;
            }
            case(OwlDataUnionOf union):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataUnionOf, [.. union.Ranges]);
                break;
            }
            case(OwlDataComplementOf complement):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataComplementOf, [complement.Range]);
                break;
            }
            case(OwlDataOneOf oneOf):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataOneOf, [.. oneOf.Literals]);
                break;
            }
            case(OwlDatatypeRestriction restriction):
            {
                List<object> items = [restriction.Datatype];
                foreach(OwlFacetRestriction facet in restriction.Restrictions)
                {
                    items.Add(facet.Facet);
                    items.Add(facet.Value);
                }

                PushFrame(work, output, OwlFunctionalKeywords.DatatypeRestriction, items);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Expands an axiom: writes its head keyword and pushes its annotations and logical parts.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="output">The destination buffer writer.</param>
    private static void ExpandAxiom(OwlAxiom axiom, Stack<Step> work, IBufferWriter<byte> output)
    {
        switch(axiom)
        {
            case(OwlDeclarationAxiom declaration):
            {
                PushFrame(work, output, OwlFunctionalKeywords.Declaration, [.. axiom.Annotations, new EntityDeclaration(declaration.Kind, declaration.Entity.Iri)]);
                break;
            }
            case(OwlSubClassOfAxiom subClass):
            {
                PushFrame(work, output, OwlFunctionalKeywords.SubClassOf, [.. axiom.Annotations, subClass.SubClass, subClass.SuperClass]);
                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                PushFrame(work, output, OwlFunctionalKeywords.EquivalentClasses, [.. axiom.Annotations, equivalent.First, equivalent.Second]);
                break;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DisjointClasses, [.. axiom.Annotations, .. disjoint.Operands]);
                break;
            }
            case(OwlDisjointUnionAxiom disjointUnion):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DisjointUnion, [.. axiom.Annotations, disjointUnion.Class, .. disjointUnion.Operands]);
                break;
            }
            case(OwlPropertyChainAxiom chain):
            {
                PushFrame(work, output, OwlFunctionalKeywords.SubObjectPropertyOf, [.. axiom.Annotations, new PropertyChainGroup([.. chain.Chain]), chain.SuperProperty]);
                break;
            }
            case(OwlSubObjectPropertyOfAxiom subProperty):
            {
                PushFrame(work, output, OwlFunctionalKeywords.SubObjectPropertyOf, [.. axiom.Annotations, subProperty.SubProperty, subProperty.SuperProperty]);
                break;
            }
            case(OwlEquivalentObjectPropertiesAxiom equivalentProperties):
            {
                PushFrame(work, output, OwlFunctionalKeywords.EquivalentObjectProperties, [.. axiom.Annotations, equivalentProperties.First, equivalentProperties.Second]);
                break;
            }
            case(OwlDisjointObjectPropertiesAxiom disjointProperties):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DisjointObjectProperties, [.. axiom.Annotations, .. disjointProperties.Operands]);
                break;
            }
            case(OwlInverseObjectPropertiesAxiom inverse):
            {
                PushFrame(work, output, OwlFunctionalKeywords.InverseObjectProperties, [.. axiom.Annotations, inverse.First, inverse.Second]);
                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectPropertyDomain, [.. axiom.Annotations, domain.Property, domain.Domain]);
                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectPropertyRange, [.. axiom.Annotations, range.Property, range.Range]);
                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                PushFrame(work, output, CharacteristicHead(characteristic.Characteristic), [.. axiom.Annotations, characteristic.Property]);
                break;
            }
            case(OwlSubDataPropertyOfAxiom subData):
            {
                PushFrame(work, output, OwlFunctionalKeywords.SubDataPropertyOf, [.. axiom.Annotations, subData.SubProperty, subData.SuperProperty]);
                break;
            }
            case(OwlEquivalentDataPropertiesAxiom equivalentData):
            {
                PushFrame(work, output, OwlFunctionalKeywords.EquivalentDataProperties, [.. axiom.Annotations, equivalentData.First, equivalentData.Second]);
                break;
            }
            case(OwlDisjointDataPropertiesAxiom disjointData):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DisjointDataProperties, [.. axiom.Annotations, .. disjointData.Operands]);
                break;
            }
            case(OwlDataPropertyDomainAxiom dataDomain):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataPropertyDomain, [.. axiom.Annotations, dataDomain.Property, dataDomain.Domain]);
                break;
            }
            case(OwlDataPropertyRangeAxiom dataRange):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataPropertyRange, [.. axiom.Annotations, dataRange.Property, dataRange.Range]);
                break;
            }
            case(OwlFunctionalDataPropertyAxiom functionalData):
            {
                PushFrame(work, output, OwlFunctionalKeywords.FunctionalDataProperty, [.. axiom.Annotations, functionalData.Property]);
                break;
            }
            case(OwlDatatypeDefinitionAxiom definition):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DatatypeDefinition, [.. axiom.Annotations, definition.Datatype, definition.Range]);
                break;
            }
            case(OwlHasKeyAxiom hasKey):
            {
                PushFrame(work, output, OwlFunctionalKeywords.HasKey, [.. axiom.Annotations, hasKey.Class, new BareGroup([.. hasKey.ObjectProperties]), new BareGroup([.. hasKey.DataProperties])]);
                break;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ClassAssertion, [.. axiom.Annotations, assertion.Class, assertion.Individual]);
                break;
            }
            case(OwlObjectPropertyAssertionAxiom objectAssertion):
            {
                PushFrame(work, output, OwlFunctionalKeywords.ObjectPropertyAssertion, [.. axiom.Annotations, objectAssertion.Property, objectAssertion.Source, objectAssertion.Target]);
                break;
            }
            case(OwlNegativeObjectPropertyAssertionAxiom negativeObject):
            {
                PushFrame(work, output, OwlFunctionalKeywords.NegativeObjectPropertyAssertion, [.. axiom.Annotations, negativeObject.Property, negativeObject.Source, negativeObject.Target]);
                break;
            }
            case(OwlDataPropertyAssertionAxiom dataAssertion):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DataPropertyAssertion, [.. axiom.Annotations, dataAssertion.Property, dataAssertion.Source, dataAssertion.Target]);
                break;
            }
            case(OwlNegativeDataPropertyAssertionAxiom negativeData):
            {
                PushFrame(work, output, OwlFunctionalKeywords.NegativeDataPropertyAssertion, [.. axiom.Annotations, negativeData.Property, negativeData.Source, negativeData.Target]);
                break;
            }
            case(OwlSameIndividualAxiom same):
            {
                PushFrame(work, output, OwlFunctionalKeywords.SameIndividual, [.. axiom.Annotations, same.First, same.Second]);
                break;
            }
            case(OwlDifferentIndividualsAxiom different):
            {
                PushFrame(work, output, OwlFunctionalKeywords.DifferentIndividuals, [.. axiom.Annotations, .. different.Individuals]);
                break;
            }
            case(OwlAnnotationAssertionAxiom annotation):
            {
                PushFrame(work, output, OwlFunctionalKeywords.AnnotationAssertion, [.. axiom.Annotations, annotation.Property, annotation.Subject, annotation.Value]);
                break;
            }
            case(OwlSubAnnotationPropertyOfAxiom subAnnotation):
            {
                PushFrame(work, output, OwlFunctionalKeywords.SubAnnotationPropertyOf, [.. axiom.Annotations, subAnnotation.SubProperty, subAnnotation.SuperProperty]);
                break;
            }
            case(OwlAnnotationPropertyDomainAxiom annotationDomain):
            {
                PushFrame(work, output, OwlFunctionalKeywords.AnnotationPropertyDomain, [.. axiom.Annotations, annotationDomain.Property, annotationDomain.Domain]);
                break;
            }
            case(OwlAnnotationPropertyRangeAxiom annotationRange):
            {
                PushFrame(work, output, OwlFunctionalKeywords.AnnotationPropertyRange, [.. axiom.Annotations, annotationRange.Property, annotationRange.Range]);
                break;
            }
            case(OwlImportAxiom import):
            {
                PushFrame(work, output, OwlFunctionalKeywords.Import, [.. axiom.Annotations, import.Imported]);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    //Writes a frame head and pushes its items: each item preceded by a
    //separating space and the frame closed by " )", so popping renders
    //head( item0 item1 … ). A bare group passes an empty head.
    private static void PushFrame(Stack<Step> work, IBufferWriter<byte> output, ReadOnlySpan<byte> head, List<object> items)
    {
        output.Write(head);
        output.Write("("u8);

        List<Step> steps = new(items.Count * 2 + 1);
        foreach(object item in items)
        {
            steps.Add(new Step(StepKind.Space, null));
            steps.Add(item is int ? new Step(StepKind.Number, item) : new Step(StepKind.Node, item));
        }

        steps.Add(new Step(StepKind.Close, null));
        PushReversed(work, steps);
    }

    /// <summary>Writes a quoted literal with its <c>\\</c>/<c>"</c> escapes and optional language or datatype suffix.</summary>
    /// <param name="value">The literal.</param>
    /// <param name="output">The destination buffer writer.</param>
    private static void WriteLiteral(Literal value, IBufferWriter<byte> output)
    {
        output.Write("\""u8);

        ReadOnlySpan<byte> lexical = value.Value.Span;
        int start = 0;
        for(int i = 0; i < lexical.Length; i++)
        {
            if(lexical[i] == (byte)'"' || lexical[i] == (byte)'\\')
            {
                output.Write(lexical[start..i]);
                output.Write("\\"u8);
                start = i;
            }
        }

        output.Write(lexical[start..]);
        output.Write("\""u8);

        if(value.Language is Utf8String language)
        {
            output.Write("@"u8);
            output.Write(language.Span);

            return;
        }

        //A plain quoted literal reads back as xsd:string; every other
        //datatype is written explicitly.
        if(!value.Datatype.Iri.Equals(Vocabulary.Xsd.String))
        {
            output.Write("^^<"u8);
            output.Write(value.Datatype.Iri.Span);
            output.Write(">"u8);
        }
    }

    /// <summary>Writes a nonnegative integer as its decimal digits.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="output">The destination buffer writer.</param>
    private static void WriteNumber(int value, IBufferWriter<byte> output)
    {
        Span<byte> buffer = output.GetSpan(16);
        Utf8Formatter.TryFormat(value, buffer, out int written);
        output.Advance(written);
    }

    /// <summary>The functional-syntax head keyword for an entity declaration kind.</summary>
    /// <param name="kind">The entity kind.</param>
    /// <returns>The keyword bytes.</returns>
    private static ReadOnlySpan<byte> DeclarationKind(OwlEntityKind kind)
    {
        return kind switch
        {
            OwlEntityKind.Class => OwlFunctionalKeywords.Class,
            OwlEntityKind.Datatype => OwlFunctionalKeywords.Datatype,
            OwlEntityKind.ObjectProperty => OwlFunctionalKeywords.ObjectProperty,
            OwlEntityKind.DataProperty => OwlFunctionalKeywords.DataProperty,
            OwlEntityKind.AnnotationProperty => OwlFunctionalKeywords.AnnotationProperty,
            _ => OwlFunctionalKeywords.NamedIndividual
        };
    }

    /// <summary>The functional-syntax head keyword for an object-property characteristic.</summary>
    /// <param name="characteristic">The characteristic.</param>
    /// <returns>The keyword bytes.</returns>
    private static ReadOnlySpan<byte> CharacteristicHead(OwlPropertyCharacteristic characteristic)
    {
        return characteristic switch
        {
            OwlPropertyCharacteristic.Functional => OwlFunctionalKeywords.FunctionalObjectProperty,
            OwlPropertyCharacteristic.InverseFunctional => OwlFunctionalKeywords.InverseFunctionalObjectProperty,
            OwlPropertyCharacteristic.Transitive => OwlFunctionalKeywords.TransitiveObjectProperty,
            OwlPropertyCharacteristic.Symmetric => OwlFunctionalKeywords.SymmetricObjectProperty,
            OwlPropertyCharacteristic.Asymmetric => OwlFunctionalKeywords.AsymmetricObjectProperty,
            OwlPropertyCharacteristic.Reflexive => OwlFunctionalKeywords.ReflexiveObjectProperty,
            _ => OwlFunctionalKeywords.IrreflexiveObjectProperty
        };
    }

    /// <summary>Pushes built steps in reverse so they pop in their built order.</summary>
    /// <param name="work">The work stack.</param>
    /// <param name="steps">The steps to push.</param>
    private static void PushReversed(Stack<Step> work, List<Step> steps)
    {
        for(int i = steps.Count - 1; i >= 0; i--)
        {
            work.Push(steps[i]);
        }
    }
}
