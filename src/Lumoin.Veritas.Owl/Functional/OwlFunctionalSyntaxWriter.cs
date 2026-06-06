using System;
using System.Collections.Generic;
using System.Globalization;
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
/// The rendering is deterministic and prefix-free: every IRI is written in
/// full angle-bracket form, one axiom per line in document order, axiom
/// annotations leading their frame. Reading a rendering and rendering the
/// result reproduces the same text — the writer's output is its own fixed
/// point.
/// </para>
/// <para>
/// Expression trees render through an explicit work stack; the no-recursion
/// discipline holds.
/// </para>
/// </remarks>
public static class OwlFunctionalSyntaxWriter
{
    /// <summary>
    /// Renders the document.
    /// </summary>
    /// <param name="document">The structural document.</param>
    /// <returns>The functional-syntax text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static string Write(OwlOntologyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        StringBuilder text = new();
        text.Append("Ontology(");
        if(document.OntologyIri is NamedNode ontologyIri)
        {
            text.Append(" <").Append(ontologyIri.Iri.ToString()).Append('>');
        }

        text.AppendLine();

        foreach(OwlAxiom axiom in document.Axioms)
        {
            text.Append("  ");
            Render(axiom, text);
            text.AppendLine();
        }

        text.AppendLine(")");

        return text.ToString();
    }

    //One unit of rendering work: literal text, or a node to expand. The
    //printer pops text straight into the output and expands nodes into
    //their head, children, and closing parenthesis, pushed in reverse.
    private readonly record struct Piece(string? Text, object? Node)
    {
        public static Piece Of(string text)
        {
            return new Piece(text, null);
        }

        public static Piece Expand(object node)
        {
            return new Piece(null, node);
        }
    }

    private static void Render(object root, StringBuilder text)
    {
        Stack<Piece> work = new();
        work.Push(Piece.Expand(root));

        while(work.Count > 0)
        {
            Piece piece = work.Pop();
            if(piece.Text is string literal)
            {
                text.Append(literal);

                continue;
            }

            ExpandNode(piece.Node!, work, text);
        }
    }

    //Expands one node: appends its leaf text directly, or pushes its frame
    //pieces in reverse so they pop in order.
    private static void ExpandNode(object node, Stack<Piece> work, StringBuilder text)
    {
        switch(node)
        {
            case NamedNode named:
            {
                text.Append('<').Append(named.Iri.ToString()).Append('>');
                break;
            }
            case BlankNode blank:
            {
                text.Append("_:").Append(blank.Label.ToString());
                break;
            }
            case Literal value:
            {
                AppendLiteral(value, text);
                break;
            }
            case OwlAnnotation annotation:
            {
                List<Piece> pieces = [Piece.Of("Annotation( ")];
                foreach(OwlAnnotation nested in annotation.Annotations)
                {
                    pieces.Add(Piece.Expand(nested));
                    pieces.Add(Piece.Of(" "));
                }

                pieces.Add(Piece.Expand(annotation.Property));
                pieces.Add(Piece.Of(" "));
                pieces.Add(Piece.Expand(annotation.Value));
                pieces.Add(Piece.Of(" )"));
                PushReversed(work, pieces);
                break;
            }
            case OwlObjectPropertyReference property:
            {
                text.Append('<').Append(property.Named.Iri.ToString()).Append('>');
                break;
            }
            case OwlInverseObjectProperty inverse:
            {
                text.Append("ObjectInverseOf( <").Append(inverse.Inverted.Iri.ToString()).Append("> )");
                break;
            }
            case OwlClassExpression expression:
            {
                ExpandClassExpression(expression, work, text);
                break;
            }
            case OwlDataRange range:
            {
                ExpandDataRange(range, work, text);
                break;
            }
            case OwlAxiom axiom:
            {
                ExpandAxiom(axiom, work);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    private static void ExpandClassExpression(OwlClassExpression expression, Stack<Piece> work, StringBuilder text)
    {
        switch(expression)
        {
            case OwlClassReference reference:
            {
                text.Append('<').Append(reference.Class.Iri.ToString()).Append('>');
                break;
            }
            case OwlObjectIntersectionOf intersection:
            {
                PushFrame(work, "ObjectIntersectionOf", [.. intersection.Operands]);
                break;
            }
            case OwlObjectUnionOf union:
            {
                PushFrame(work, "ObjectUnionOf", [.. union.Operands]);
                break;
            }
            case OwlObjectComplementOf complement:
            {
                PushFrame(work, "ObjectComplementOf", [complement.Operand]);
                break;
            }
            case OwlObjectOneOf oneOf:
            {
                PushFrame(work, "ObjectOneOf", [.. oneOf.Individuals]);
                break;
            }
            case OwlObjectSomeValuesFrom someValues:
            {
                PushFrame(work, "ObjectSomeValuesFrom", [someValues.Property, someValues.Filler]);
                break;
            }
            case OwlObjectAllValuesFrom allValues:
            {
                PushFrame(work, "ObjectAllValuesFrom", [allValues.Property, allValues.Filler]);
                break;
            }
            case OwlObjectHasValue hasValue:
            {
                PushFrame(work, "ObjectHasValue", [hasValue.Property, hasValue.Individual]);
                break;
            }
            case OwlObjectHasSelf hasSelf:
            {
                PushFrame(work, "ObjectHasSelf", [hasSelf.Property]);
                break;
            }
            case OwlObjectCardinality cardinality:
            {
                string head = cardinality.Kind switch
                {
                    OwlCardinalityKind.Min => "ObjectMinCardinality",
                    OwlCardinalityKind.Max => "ObjectMaxCardinality",
                    _ => "ObjectExactCardinality"
                };

                List<object> parts = [cardinality.Cardinality.ToString(CultureInfo.InvariantCulture), cardinality.Property];
                if(cardinality.Filler is OwlClassExpression filler)
                {
                    parts.Add(filler);
                }

                PushFrame(work, head, parts);
                break;
            }
            case OwlDataSomeValuesFrom dataSome:
            {
                List<object> parts = [.. dataSome.Properties, dataSome.Range];
                PushFrame(work, "DataSomeValuesFrom", parts);
                break;
            }
            case OwlDataAllValuesFrom dataAll:
            {
                List<object> parts = [.. dataAll.Properties, dataAll.Range];
                PushFrame(work, "DataAllValuesFrom", parts);
                break;
            }
            case OwlDataHasValue dataHasValue:
            {
                PushFrame(work, "DataHasValue", [dataHasValue.Property, dataHasValue.Value]);
                break;
            }
            case OwlDataCardinality dataCardinality:
            {
                string head = dataCardinality.Kind switch
                {
                    OwlCardinalityKind.Min => "DataMinCardinality",
                    OwlCardinalityKind.Max => "DataMaxCardinality",
                    _ => "DataExactCardinality"
                };

                List<object> parts = [dataCardinality.Cardinality.ToString(CultureInfo.InvariantCulture), dataCardinality.Property];
                if(dataCardinality.Range is OwlDataRange qualifier)
                {
                    parts.Add(qualifier);
                }

                PushFrame(work, head, parts);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    private static void ExpandDataRange(OwlDataRange range, Stack<Piece> work, StringBuilder text)
    {
        switch(range)
        {
            case OwlDatatypeReference reference:
            {
                text.Append('<').Append(reference.Datatype.Iri.ToString()).Append('>');
                break;
            }
            case OwlDataIntersectionOf intersection:
            {
                PushFrame(work, "DataIntersectionOf", [.. intersection.Ranges]);
                break;
            }
            case OwlDataUnionOf union:
            {
                PushFrame(work, "DataUnionOf", [.. union.Ranges]);
                break;
            }
            case OwlDataComplementOf complement:
            {
                PushFrame(work, "DataComplementOf", [complement.Range]);
                break;
            }
            case OwlDataOneOf oneOf:
            {
                PushFrame(work, "DataOneOf", [.. oneOf.Literals]);
                break;
            }
            case OwlDatatypeRestriction restriction:
            {
                List<object> parts = [restriction.Datatype];
                foreach(OwlFacetRestriction facet in restriction.Restrictions)
                {
                    parts.Add(facet.Facet);
                    parts.Add(facet.Value);
                }

                PushFrame(work, "DatatypeRestriction", parts);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    private static void ExpandAxiom(OwlAxiom axiom, Stack<Piece> work)
    {
        (string head, List<object> parts) = axiom switch
        {
            OwlDeclarationAxiom declaration => ("Declaration", [DeclarationEntity(declaration)]),
            OwlSubClassOfAxiom subClass => ("SubClassOf", [subClass.SubClass, subClass.SuperClass]),
            OwlEquivalentClassesAxiom equivalent => ("EquivalentClasses", [equivalent.First, equivalent.Second]),
            OwlDisjointClassesAxiom disjoint => ("DisjointClasses", [.. disjoint.Operands]),
            OwlDisjointUnionAxiom disjointUnion => ("DisjointUnion", [disjointUnion.Class, .. disjointUnion.Operands]),
            OwlPropertyChainAxiom chain => ("SubObjectPropertyOf", [Group("ObjectPropertyChain", [.. chain.Chain]), chain.SuperProperty]),
            OwlSubObjectPropertyOfAxiom subProperty => ("SubObjectPropertyOf", [subProperty.SubProperty, subProperty.SuperProperty]),
            OwlEquivalentObjectPropertiesAxiom equivalentProperties => ("EquivalentObjectProperties", [equivalentProperties.First, equivalentProperties.Second]),
            OwlDisjointObjectPropertiesAxiom disjointProperties => ("DisjointObjectProperties", [.. disjointProperties.Operands]),
            OwlInverseObjectPropertiesAxiom inverse => ("InverseObjectProperties", [inverse.First, inverse.Second]),
            OwlObjectPropertyDomainAxiom domain => ("ObjectPropertyDomain", [domain.Property, domain.Domain]),
            OwlObjectPropertyRangeAxiom range => ("ObjectPropertyRange", [range.Property, range.Range]),
            OwlObjectPropertyCharacteristicAxiom characteristic => (CharacteristicHead(characteristic.Characteristic), [characteristic.Property]),
            OwlSubDataPropertyOfAxiom subData => ("SubDataPropertyOf", [subData.SubProperty, subData.SuperProperty]),
            OwlEquivalentDataPropertiesAxiom equivalentData => ("EquivalentDataProperties", [equivalentData.First, equivalentData.Second]),
            OwlDisjointDataPropertiesAxiom disjointData => ("DisjointDataProperties", [.. disjointData.Operands]),
            OwlDataPropertyDomainAxiom dataDomain => ("DataPropertyDomain", [dataDomain.Property, dataDomain.Domain]),
            OwlDataPropertyRangeAxiom dataRange => ("DataPropertyRange", [dataRange.Property, dataRange.Range]),
            OwlFunctionalDataPropertyAxiom functionalData => ("FunctionalDataProperty", [functionalData.Property]),
            OwlDatatypeDefinitionAxiom definition => ("DatatypeDefinition", [definition.Datatype, definition.Range]),
            OwlHasKeyAxiom hasKey => ("HasKey", [hasKey.Class, Group(null, [.. hasKey.ObjectProperties]), Group(null, [.. hasKey.DataProperties])]),
            OwlClassAssertionAxiom assertion => ("ClassAssertion", [assertion.Class, assertion.Individual]),
            OwlObjectPropertyAssertionAxiom objectAssertion => ("ObjectPropertyAssertion", [objectAssertion.Property, objectAssertion.Source, objectAssertion.Target]),
            OwlNegativeObjectPropertyAssertionAxiom negativeObject => ("NegativeObjectPropertyAssertion", [negativeObject.Property, negativeObject.Source, negativeObject.Target]),
            OwlDataPropertyAssertionAxiom dataAssertion => ("DataPropertyAssertion", [dataAssertion.Property, dataAssertion.Source, dataAssertion.Target]),
            OwlNegativeDataPropertyAssertionAxiom negativeData => ("NegativeDataPropertyAssertion", [negativeData.Property, negativeData.Source, negativeData.Target]),
            OwlSameIndividualAxiom same => ("SameIndividual", [same.First, same.Second]),
            OwlDifferentIndividualsAxiom different => ("DifferentIndividuals", [.. different.Individuals]),
            OwlAnnotationAssertionAxiom annotation => ("AnnotationAssertion", [annotation.Property, annotation.Subject, annotation.Value]),
            OwlSubAnnotationPropertyOfAxiom subAnnotation => ("SubAnnotationPropertyOf", [subAnnotation.SubProperty, subAnnotation.SuperProperty]),
            OwlAnnotationPropertyDomainAxiom annotationDomain => ("AnnotationPropertyDomain", [annotationDomain.Property, annotationDomain.Domain]),
            OwlAnnotationPropertyRangeAxiom annotationRange => ("AnnotationPropertyRange", [annotationRange.Property, annotationRange.Range]),
            OwlImportAxiom import => ("Import", [import.Imported]),
            _ => (string.Empty, new List<object>())
        };

        if(head.Length == 0)
        {
            return;
        }

        List<Piece> pieces = [Piece.Of(head), Piece.Of("( ")];
        foreach(OwlAnnotation annotation in axiom.Annotations)
        {
            pieces.Add(Piece.Expand(annotation));
            pieces.Add(Piece.Of(" "));
        }

        for(int i = 0; i < parts.Count; i++)
        {
            if(i > 0)
            {
                pieces.Add(Piece.Of(" "));
            }

            pieces.Add(parts[i] is string verbatim ? Piece.Of(verbatim) : Piece.Expand(parts[i]));
        }

        pieces.Add(Piece.Of(" )"));
        PushReversed(work, pieces);
    }

    //A nested fixed-arity group rendered eagerly into one verbatim part:
    //its members render through their own stack walk first.
    private static string Group(string? head, List<object> members)
    {
        StringBuilder text = new();
        if(head is not null)
        {
            text.Append(head);
        }

        text.Append("( ");
        for(int i = 0; i < members.Count; i++)
        {
            if(i > 0)
            {
                text.Append(' ');
            }

            Render(members[i], text);
        }

        text.Append(" )");

        return text.ToString();
    }

    private static string DeclarationEntity(OwlDeclarationAxiom declaration)
    {
        string kind = declaration.Kind switch
        {
            OwlEntityKind.Class => "Class",
            OwlEntityKind.Datatype => "Datatype",
            OwlEntityKind.ObjectProperty => "ObjectProperty",
            OwlEntityKind.DataProperty => "DataProperty",
            OwlEntityKind.AnnotationProperty => "AnnotationProperty",
            _ => "NamedIndividual"
        };

        return $"{kind}( <{declaration.Entity.Iri}> )";
    }

    private static string CharacteristicHead(OwlPropertyCharacteristic characteristic)
    {
        return characteristic switch
        {
            OwlPropertyCharacteristic.Functional => "FunctionalObjectProperty",
            OwlPropertyCharacteristic.InverseFunctional => "InverseFunctionalObjectProperty",
            OwlPropertyCharacteristic.Transitive => "TransitiveObjectProperty",
            OwlPropertyCharacteristic.Symmetric => "SymmetricObjectProperty",
            OwlPropertyCharacteristic.Asymmetric => "AsymmetricObjectProperty",
            OwlPropertyCharacteristic.Reflexive => "ReflexiveObjectProperty",
            _ => "IrreflexiveObjectProperty"
        };
    }

    private static void AppendLiteral(Literal value, StringBuilder text)
    {
        text.Append('"');
        string lexical = value.Value.ToString();
        foreach(char character in lexical)
        {
            if(character is '"' or '\\')
            {
                text.Append('\\');
            }

            text.Append(character);
        }

        text.Append('"');

        if(value.Language is Utf8String language)
        {
            text.Append('@').Append(language.ToString());

            return;
        }

        //A plain quoted literal reads back as xsd:string; every other
        //datatype is written explicitly.
        if(!value.Datatype.Iri.Equals(Vocabulary.Xsd.String))
        {
            text.Append("^^<").Append(value.Datatype.Iri.ToString()).Append('>');
        }
    }

    private static void PushFrame(Stack<Piece> work, string head, List<object> parts)
    {
        List<Piece> pieces = [Piece.Of(head), Piece.Of("( ")];
        for(int i = 0; i < parts.Count; i++)
        {
            if(i > 0)
            {
                pieces.Add(Piece.Of(" "));
            }

            pieces.Add(parts[i] is string verbatim ? Piece.Of(verbatim) : Piece.Expand(parts[i]));
        }

        pieces.Add(Piece.Of(" )"));
        PushReversed(work, pieces);
    }

    private static void PushReversed(Stack<Piece> work, List<Piece> pieces)
    {
        for(int i = pieces.Count - 1; i >= 0; i--)
        {
            work.Push(pieces[i]);
        }
    }
}
