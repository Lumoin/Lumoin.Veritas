using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>
/// Writes a structural document in OWL 2 Manchester syntax — the reverse of
/// <see cref="OwlManchesterSyntaxReader"/>, completing the text round-trip
/// for everything the frame-based syntax can express.
/// </summary>
/// <remarks>
/// <para>
/// The rendering is deterministic and prefix-free: every IRI is written in
/// full angle-bracket form; axioms group into frames in order of first
/// appearance; each section line carries one axiom, its annotations leading
/// the payload, so the annotated-list grammar stays unambiguous. Reading a
/// rendering and rendering the result reproduces the same text.
/// </para>
/// <para>
/// Manchester syntax cannot express every axiom: a general class inclusion
/// whose subclass is anonymous, an axiom anchored on an inverse property
/// expression, an annotated declaration, or an annotation assertion on an
/// IRI no frame declares. Such axioms are recorded into the supplied
/// diagnostic bag as warnings and skipped — the writer never throws on
/// content.
/// </para>
/// <para>
/// Expression trees render through an explicit work stack; the no-recursion
/// discipline holds. Compound operands and fillers are parenthesized, so
/// operator precedence is explicit in the text.
/// </para>
/// </remarks>
public static class OwlManchesterSyntaxWriter
{
    /// <summary>
    /// Renders the document.
    /// </summary>
    /// <param name="document">The structural document.</param>
    /// <param name="diagnostics">The bag inexpressible-axiom warnings record into.</param>
    /// <returns>The Manchester-syntax text.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Write(OwlOntologyDocument document, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Plan plan = new(document.OntologyIri, diagnostics);

        foreach(OwlAxiom axiom in document.Axioms)
        {
            PlaceAxiom(plan, axiom);
        }

        plan.PlacePendingAnnotations();

        return plan.Render();
    }

    //One frame section line: optional annotations and a payload.
    private sealed record SectionLine(ImmutableAnnotations Annotations, string Payload);

    //The annotations of one line, kept as the structural values until rendering.
    private sealed record ImmutableAnnotations(System.Collections.Immutable.ImmutableArray<OwlAnnotation> Values);

    //One frame: its keyword, its subject text, and its section lines in order.
    private sealed class Frame
    {
        /// <summary>The frame keyword, such as <c>Class:</c>.</summary>
        public required string Keyword { get; init; }

        /// <summary>The rendered subject term.</summary>
        public required string Subject { get; init; }

        /// <summary>The section lines, in placement order.</summary>
        public List<(string Section, SectionLine Line)> Lines { get; } = [];
    }

    //The document plan: header, frames in first-appearance order, misc lines.
    private sealed class Plan
    {
        public Plan(NamedNode? ontologyIri, DiagnosticBag diagnostics)
        {
            OntologyIri = ontologyIri;
            Diagnostics = diagnostics;
        }

        /// <summary>The ontology IRI, when the document names one.</summary>
        public NamedNode? OntologyIri { get; }

        /// <summary>The bag inexpressibility warnings record into.</summary>
        public DiagnosticBag Diagnostics { get; }

        /// <summary>The rendered import IRIs, in document order.</summary>
        public List<string> Imports { get; } = [];

        /// <summary>The ontology-level annotations, in document order.</summary>
        public List<OwlAnnotation> OntologyAnnotations { get; } = [];

        /// <summary>The frames, keyed by keyword and subject, in first-appearance order.</summary>
        public Dictionary<(string Keyword, string Subject), Frame> Frames { get; } = [];

        /// <summary>The frame keys in first-appearance order.</summary>
        public List<(string Keyword, string Subject)> FrameOrder { get; } = [];

        /// <summary>The entity-free n-ary lines, in document order.</summary>
        public List<(string Keyword, SectionLine Line)> MiscLines { get; } = [];

        /// <summary>Annotation assertions awaiting a frame to host them, in document order.</summary>
        public List<OwlAnnotationAssertionAxiom> PendingAnnotations { get; } = [];

        /// <summary>Finds or creates the frame for a subject.</summary>
        /// <param name="keyword">The frame keyword.</param>
        /// <param name="subject">The rendered subject.</param>
        /// <returns>The frame.</returns>
        public Frame EnsureFrame(string keyword, string subject)
        {
            if(!Frames.TryGetValue((keyword, subject), out Frame? frame))
            {
                frame = new Frame { Keyword = keyword, Subject = subject };
                Frames[(keyword, subject)] = frame;
                FrameOrder.Add((keyword, subject));
            }

            return frame;
        }

        /// <summary>Adds one section line to a frame, unless the payload contains an inexpressible sub-expression.</summary>
        /// <param name="keyword">The frame keyword.</param>
        /// <param name="subject">The rendered subject.</param>
        /// <param name="section">The section keyword.</param>
        /// <param name="axiom">The axiom whose annotations lead the line.</param>
        /// <param name="payload">The rendered payload.</param>
        public void AddLine(string keyword, string subject, string section, OwlAxiom axiom, string payload)
        {
            //An expression renderer that met an n-ary data quantifier (or
            //another shape with no Manchester form) embedded the marker; the
            //whole axiom is skipped rather than silently altered.
            if(payload.Contains("urn:veritas:inexpressible", StringComparison.Ordinal))
            {
                Skip("an expression with no Manchester form");

                return;
            }

            EnsureFrame(keyword, subject).Lines.Add((section, new SectionLine(new ImmutableAnnotations(axiom.Annotations), payload)));
        }

        /// <summary>Records an inexpressible axiom and skips it.</summary>
        /// <param name="reason">What the syntax cannot express.</param>
        public void Skip(string reason)
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.UnsupportedConstruct,
                DiagnosticSeverity.Warning,
                default,
                Utf8String.FromString($"Manchester syntax cannot express this axiom: {reason}")));
        }

        /// <summary>
        /// Places the deferred annotation assertions: the ontology IRI's go to
        /// the header block, others to any frame declaring the subject; a
        /// subject with no frame records a warning.
        /// </summary>
        public void PlacePendingAnnotations()
        {
            foreach(OwlAnnotationAssertionAxiom axiom in PendingAnnotations)
            {
                if(OntologyIri is NamedNode ontologyIri && axiom.Subject is NamedNode subjectIri && subjectIri.Iri.Equals(ontologyIri.Iri))
                {
                    OntologyAnnotations.Add(new OwlAnnotation(axiom.Property, axiom.Value) { Annotations = axiom.Annotations });

                    continue;
                }

                string subject = RenderTerm(axiom.Subject);
                Frame? host = null;
                foreach((string Keyword, string Subject) key in FrameOrder)
                {
                    if(key.Subject == subject)
                    {
                        host = Frames[key];
                        break;
                    }
                }

                if(host is null)
                {
                    Skip("an annotation assertion on an IRI no frame declares");

                    continue;
                }

                host.Lines.Add(("Annotations:", new SectionLine(
                    new ImmutableAnnotations(axiom.Annotations),
                    $"{RenderTerm(axiom.Property)} {RenderTerm(axiom.Value)}")));
            }
        }

        /// <summary>Renders the plan to the document text.</summary>
        /// <returns>The Manchester-syntax text.</returns>
        public string Render()
        {
            StringBuilder text = new();
            text.Append("Ontology:");
            if(OntologyIri is NamedNode iri)
            {
                text.Append(" <").Append(iri.Iri.ToString()).Append('>');
            }

            text.AppendLine();

            foreach(string import in Imports)
            {
                text.Append("Import: ").Append(import).AppendLine();
            }

            foreach(OwlAnnotation annotation in OntologyAnnotations)
            {
                text.Append("Annotations: ");
                AppendAnnotation(text, annotation);
                text.AppendLine();
            }

            foreach((string Keyword, string Subject) key in FrameOrder)
            {
                Frame frame = Frames[key];
                text.AppendLine();
                text.Append(frame.Keyword).Append(' ').Append(frame.Subject).AppendLine();

                foreach((string section, SectionLine line) in frame.Lines)
                {
                    text.Append("    ").Append(section).Append(' ');
                    AppendLineAnnotations(text, line.Annotations);
                    text.Append(line.Payload).AppendLine();
                }
            }

            foreach((string keyword, SectionLine line) in MiscLines)
            {
                text.AppendLine();
                text.Append(keyword).Append(' ');
                AppendLineAnnotations(text, line.Annotations);
                text.Append(line.Payload).AppendLine();
            }

            return text.ToString();
        }

        /// <summary>Appends a line's leading <c>Annotations:</c> block, when it has one.</summary>
        /// <param name="text">The output.</param>
        /// <param name="annotations">The line's annotations.</param>
        private static void AppendLineAnnotations(StringBuilder text, ImmutableAnnotations annotations)
        {
            if(annotations.Values.IsDefaultOrEmpty)
            {
                return;
            }

            text.Append("Annotations: ");
            for(int i = 0; i < annotations.Values.Length; i++)
            {
                if(i > 0)
                {
                    text.Append(", ");
                }

                AppendAnnotation(text, annotations.Values[i]);
            }

            text.Append(' ');
        }

        /// <summary>Appends one annotation: its nested block, its property, and its target.</summary>
        /// <param name="text">The output.</param>
        /// <param name="annotation">The annotation.</param>
        private static void AppendAnnotation(StringBuilder text, OwlAnnotation annotation)
        {
            //Nested annotation blocks recurse structurally; an explicit stack
            //of pending pieces keeps the call stack flat.
            Stack<object> work = new();
            work.Push(annotation);

            while(work.Count > 0)
            {
                object piece = work.Pop();
                if(piece is string literal)
                {
                    text.Append(literal);

                    continue;
                }

                OwlAnnotation current = (OwlAnnotation)piece;
                work.Push($"{RenderTerm(current.Property)} {RenderTerm(current.Value)}");

                for(int i = current.Annotations.Length - 1; i >= 0; i--)
                {
                    work.Push(" ");
                    work.Push(current.Annotations[i]);
                    if(i == 0)
                    {
                        work.Push("Annotations: ");
                    }
                    else
                    {
                        //Continuing annotations join the same nested block.
                        work.Push(", ");
                    }
                }
            }
        }
    }

    //Axiom placement: each axiom either lands as one frame section line, a
    //misc line, a header entry, or a recorded skip.
    private static void PlaceAxiom(Plan plan, OwlAxiom axiom)
    {
        switch(axiom)
        {
            case OwlImportAxiom import:
            {
                plan.Imports.Add($"<{import.Imported.Iri}>");
                break;
            }

            case OwlDeclarationAxiom declaration:
            {
                if(!declaration.Annotations.IsDefaultOrEmpty)
                {
                    plan.Skip("an annotated declaration");
                    break;
                }

                plan.EnsureFrame(FrameKeyword(declaration.Kind), $"<{declaration.Entity.Iri}>");
                break;
            }

            case OwlAnnotationAssertionAxiom annotation:
            {
                plan.PendingAnnotations.Add(annotation);
                break;
            }

            case OwlSubClassOfAxiom subClass:
            {
                if(subClass.SubClass is not OwlClassReference reference)
                {
                    plan.Skip("a general class inclusion whose subclass is anonymous");
                    break;
                }

                plan.AddLine("Class:", RenderTerm(reference.Class), "SubClassOf:", axiom, RenderClass(subClass.SuperClass, parenthesize: false));
                break;
            }

            case OwlEquivalentClassesAxiom equivalent:
            {
                if(equivalent.First is OwlClassReference firstReference)
                {
                    plan.AddLine("Class:", RenderTerm(firstReference.Class), "EquivalentTo:", axiom, RenderClass(equivalent.Second, parenthesize: false));
                }
                else if(equivalent.Second is OwlClassReference secondReference)
                {
                    plan.AddLine("Class:", RenderTerm(secondReference.Class), "EquivalentTo:", axiom, RenderClass(equivalent.First, parenthesize: false));
                }
                else
                {
                    plan.MiscLines.Add(("EquivalentClasses:", new SectionLine(
                        new ImmutableAnnotations(axiom.Annotations),
                        $"{RenderClass(equivalent.First, parenthesize: false)}, {RenderClass(equivalent.Second, parenthesize: false)}")));
                }

                break;
            }

            case OwlDisjointClassesAxiom disjoint:
            {
                if(disjoint.Operands.Count == 2 && disjoint.Operands[0] is OwlClassReference firstReference)
                {
                    plan.AddLine("Class:", RenderTerm(firstReference.Class), "DisjointWith:", axiom, RenderClass(disjoint.Operands[1], parenthesize: false));
                    break;
                }

                plan.MiscLines.Add(("DisjointClasses:", new SectionLine(
                    new ImmutableAnnotations(axiom.Annotations),
                    JoinClasses(disjoint.Operands))));
                break;
            }

            case OwlDisjointUnionAxiom disjointUnion:
            {
                plan.AddLine("Class:", $"<{disjointUnion.Class.Iri}>", "DisjointUnionOf:", axiom, JoinClasses(disjointUnion.Operands));
                break;
            }

            case OwlHasKeyAxiom hasKey:
            {
                if(hasKey.Class is not OwlClassReference reference)
                {
                    plan.Skip("a key on an anonymous class expression");
                    break;
                }

                List<string> keys = [];
                foreach(OwlObjectPropertyExpression objectKey in hasKey.ObjectProperties)
                {
                    keys.Add(RenderObjectProperty(objectKey));
                }

                foreach(NamedNode dataKey in hasKey.DataProperties)
                {
                    keys.Add($"<{dataKey.Iri}>");
                }

                plan.AddLine("Class:", RenderTerm(reference.Class), "HasKey:", axiom, string.Join(" ", keys));
                break;
            }

            case OwlPropertyChainAxiom chain:
            {
                if(chain.SuperProperty is not OwlObjectPropertyReference superReference)
                {
                    plan.Skip("a property chain whose super property is an inverse expression");
                    break;
                }

                List<string> links = [];
                foreach(OwlObjectPropertyExpression link in chain.Chain)
                {
                    links.Add(RenderObjectProperty(link));
                }

                plan.AddLine("ObjectProperty:", RenderTerm(superReference.Named), "SubPropertyChain:", axiom, string.Join(" o ", links));
                break;
            }

            case OwlSubObjectPropertyOfAxiom subProperty:
            {
                if(subProperty.SubProperty is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a sub-property axiom whose subproperty is an inverse expression");
                    break;
                }

                plan.AddLine("ObjectProperty:", RenderTerm(reference.Named), "SubPropertyOf:", axiom, RenderObjectProperty(subProperty.SuperProperty));
                break;
            }

            case OwlEquivalentObjectPropertiesAxiom equivalentProperties:
            {
                if(equivalentProperties.First is OwlObjectPropertyReference firstReference)
                {
                    plan.AddLine("ObjectProperty:", RenderTerm(firstReference.Named), "EquivalentTo:", axiom, RenderObjectProperty(equivalentProperties.Second));
                }
                else if(equivalentProperties.Second is OwlObjectPropertyReference secondReference)
                {
                    plan.AddLine("ObjectProperty:", RenderTerm(secondReference.Named), "EquivalentTo:", axiom, RenderObjectProperty(equivalentProperties.First));
                }
                else
                {
                    plan.Skip("a property equivalence between two inverse expressions");
                }

                break;
            }

            case OwlDisjointObjectPropertiesAxiom disjointProperties:
            {
                if(disjointProperties.Operands.Count == 2 && disjointProperties.Operands[0] is OwlObjectPropertyReference firstReference)
                {
                    plan.AddLine("ObjectProperty:", RenderTerm(firstReference.Named), "DisjointWith:", axiom, RenderObjectProperty(disjointProperties.Operands[1]));
                    break;
                }

                List<string> properties = [];
                bool expressible = true;
                foreach(OwlObjectPropertyExpression operand in disjointProperties.Operands)
                {
                    if(operand is not OwlObjectPropertyReference operandReference)
                    {
                        expressible = false;
                        break;
                    }

                    properties.Add(RenderTerm(operandReference.Named));
                }

                if(!expressible)
                {
                    plan.Skip("a property disjointness over inverse expressions");
                    break;
                }

                plan.MiscLines.Add(("DisjointProperties:", new SectionLine(new ImmutableAnnotations(axiom.Annotations), string.Join(", ", properties))));
                break;
            }

            case OwlInverseObjectPropertiesAxiom inverse:
            {
                if(inverse.First is OwlObjectPropertyReference firstReference)
                {
                    plan.AddLine("ObjectProperty:", RenderTerm(firstReference.Named), "InverseOf:", axiom, RenderObjectProperty(inverse.Second));
                }
                else if(inverse.Second is OwlObjectPropertyReference secondReference)
                {
                    plan.AddLine("ObjectProperty:", RenderTerm(secondReference.Named), "InverseOf:", axiom, RenderObjectProperty(inverse.First));
                }
                else
                {
                    plan.Skip("an inverse-properties axiom between two inverse expressions");
                }

                break;
            }

            case OwlObjectPropertyDomainAxiom domain:
            {
                if(domain.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a domain on an inverse property expression");
                    break;
                }

                plan.AddLine("ObjectProperty:", RenderTerm(reference.Named), "Domain:", axiom, RenderClass(domain.Domain, parenthesize: false));
                break;
            }

            case OwlObjectPropertyRangeAxiom range:
            {
                if(range.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a range on an inverse property expression");
                    break;
                }

                plan.AddLine("ObjectProperty:", RenderTerm(reference.Named), "Range:", axiom, RenderClass(range.Range, parenthesize: false));
                break;
            }

            case OwlObjectPropertyCharacteristicAxiom characteristic:
            {
                if(characteristic.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a characteristic on an inverse property expression");
                    break;
                }

                plan.AddLine("ObjectProperty:", RenderTerm(reference.Named), "Characteristics:", axiom, CharacteristicWord(characteristic.Characteristic));
                break;
            }

            case OwlSubDataPropertyOfAxiom subData:
            {
                plan.AddLine("DataProperty:", $"<{subData.SubProperty.Iri}>", "SubPropertyOf:", axiom, $"<{subData.SuperProperty.Iri}>");
                break;
            }

            case OwlEquivalentDataPropertiesAxiom equivalentData:
            {
                plan.AddLine("DataProperty:", $"<{equivalentData.First.Iri}>", "EquivalentTo:", axiom, $"<{equivalentData.Second.Iri}>");
                break;
            }

            case OwlDisjointDataPropertiesAxiom disjointData:
            {
                if(disjointData.Operands.Count == 2)
                {
                    plan.AddLine("DataProperty:", $"<{disjointData.Operands[0].Iri}>", "DisjointWith:", axiom, $"<{disjointData.Operands[1].Iri}>");
                    break;
                }

                List<string> properties = [];
                foreach(NamedNode operand in disjointData.Operands)
                {
                    properties.Add($"<{operand.Iri}>");
                }

                plan.MiscLines.Add(("DisjointProperties:", new SectionLine(new ImmutableAnnotations(axiom.Annotations), string.Join(", ", properties))));
                break;
            }

            case OwlDataPropertyDomainAxiom dataDomain:
            {
                plan.AddLine("DataProperty:", $"<{dataDomain.Property.Iri}>", "Domain:", axiom, RenderClass(dataDomain.Domain, parenthesize: false));
                break;
            }

            case OwlDataPropertyRangeAxiom dataRange:
            {
                plan.AddLine("DataProperty:", $"<{dataRange.Property.Iri}>", "Range:", axiom, RenderDataRange(dataRange.Range, parenthesize: false));
                break;
            }

            case OwlFunctionalDataPropertyAxiom functionalData:
            {
                plan.AddLine("DataProperty:", $"<{functionalData.Property.Iri}>", "Characteristics:", axiom, "Functional");
                break;
            }

            case OwlDatatypeDefinitionAxiom definition:
            {
                plan.AddLine("Datatype:", $"<{definition.Datatype.Iri}>", "EquivalentTo:", axiom, RenderDataRange(definition.Range, parenthesize: false));
                break;
            }

            case OwlSubAnnotationPropertyOfAxiom subAnnotation:
            {
                plan.AddLine("AnnotationProperty:", $"<{subAnnotation.SubProperty.Iri}>", "SubPropertyOf:", axiom, $"<{subAnnotation.SuperProperty.Iri}>");
                break;
            }

            case OwlAnnotationPropertyDomainAxiom annotationDomain:
            {
                plan.AddLine("AnnotationProperty:", $"<{annotationDomain.Property.Iri}>", "Domain:", axiom, $"<{annotationDomain.Domain.Iri}>");
                break;
            }

            case OwlAnnotationPropertyRangeAxiom annotationRange:
            {
                plan.AddLine("AnnotationProperty:", $"<{annotationRange.Property.Iri}>", "Range:", axiom, $"<{annotationRange.Range.Iri}>");
                break;
            }

            case OwlClassAssertionAxiom assertion:
            {
                plan.AddLine("Individual:", RenderTerm(assertion.Individual), "Types:", axiom, RenderClass(assertion.Class, parenthesize: false));
                break;
            }

            case OwlObjectPropertyAssertionAxiom objectAssertion:
            {
                plan.AddLine("Individual:", RenderTerm(objectAssertion.Source), "Facts:", axiom, $"<{objectAssertion.Property.Iri}> {RenderTerm(objectAssertion.Target)}");
                break;
            }

            case OwlNegativeObjectPropertyAssertionAxiom negativeObject:
            {
                if(negativeObject.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a negative assertion on an inverse property expression");
                    break;
                }

                plan.AddLine("Individual:", RenderTerm(negativeObject.Source), "Facts:", axiom, $"not <{reference.Named.Iri}> {RenderTerm(negativeObject.Target)}");
                break;
            }

            case OwlDataPropertyAssertionAxiom dataAssertion:
            {
                plan.AddLine("Individual:", RenderTerm(dataAssertion.Source), "Facts:", axiom, $"<{dataAssertion.Property.Iri}> {RenderTerm(dataAssertion.Target)}");
                break;
            }

            case OwlNegativeDataPropertyAssertionAxiom negativeData:
            {
                plan.AddLine("Individual:", RenderTerm(negativeData.Source), "Facts:", axiom, $"not <{negativeData.Property.Iri}> {RenderTerm(negativeData.Target)}");
                break;
            }

            case OwlSameIndividualAxiom same:
            {
                plan.AddLine("Individual:", RenderTerm(same.First), "SameAs:", axiom, RenderTerm(same.Second));
                break;
            }

            case OwlDifferentIndividualsAxiom different:
            {
                if(different.Individuals.Count == 2)
                {
                    plan.AddLine("Individual:", RenderTerm(different.Individuals[0]), "DifferentFrom:", axiom, RenderTerm(different.Individuals[1]));
                    break;
                }

                List<string> individuals = [];
                foreach(RdfTerm individual in different.Individuals)
                {
                    individuals.Add(RenderTerm(individual));
                }

                plan.MiscLines.Add(("DifferentIndividuals:", new SectionLine(new ImmutableAnnotations(axiom.Annotations), string.Join(", ", individuals))));
                break;
            }

            default:
            {
                plan.Skip($"an axiom of kind {axiom.GetType().Name}");
                break;
            }
        }
    }

    private static string FrameKeyword(OwlEntityKind kind)
    {
        return kind switch
        {
            OwlEntityKind.Class => "Class:",
            OwlEntityKind.Datatype => "Datatype:",
            OwlEntityKind.ObjectProperty => "ObjectProperty:",
            OwlEntityKind.DataProperty => "DataProperty:",
            OwlEntityKind.AnnotationProperty => "AnnotationProperty:",
            _ => "Individual:"
        };
    }

    private static string CharacteristicWord(OwlPropertyCharacteristic characteristic)
    {
        return characteristic switch
        {
            OwlPropertyCharacteristic.Functional => "Functional",
            OwlPropertyCharacteristic.InverseFunctional => "InverseFunctional",
            OwlPropertyCharacteristic.Transitive => "Transitive",
            OwlPropertyCharacteristic.Symmetric => "Symmetric",
            OwlPropertyCharacteristic.Asymmetric => "Asymmetric",
            OwlPropertyCharacteristic.Reflexive => "Reflexive",
            _ => "Irreflexive"
        };
    }

    private static string JoinClasses(IReadOnlyList<OwlClassExpression> expressions)
    {
        List<string> parts = new(expressions.Count);
        foreach(OwlClassExpression expression in expressions)
        {
            parts.Add(RenderClass(expression, parenthesize: false));
        }

        return string.Join(", ", parts);
    }

    //Term rendering: full IRIs, blank labels, and literals in the same
    //lexical policy the functional-syntax writer uses.
    private static string RenderTerm(RdfTerm term)
    {
        StringBuilder text = new();
        AppendTerm(text, term);

        return text.ToString();
    }

    private static void AppendTerm(StringBuilder text, RdfTerm term)
    {
        switch(term)
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
                AppendLiteral(text, value);
                break;
            }

            default:
            {
                break;
            }
        }
    }

    private static void AppendLiteral(StringBuilder text, Literal value)
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

    private static string RenderObjectProperty(OwlObjectPropertyExpression expression)
    {
        return expression switch
        {
            OwlObjectPropertyReference reference => $"<{reference.Named.Iri}>",
            OwlInverseObjectProperty inverse => $"inverse <{inverse.Inverted.Iri}>",
            _ => "inverse <urn:veritas:invalid>"
        };
    }

    //One unit of expression-rendering work: literal text, a class expression
    //to expand, or a data range to expand. Parenthesization is decided at
    //push time, so precedence is explicit in the output.
    private readonly record struct Piece(string? Text, OwlClassExpression? Class, OwlDataRange? Range, bool Parenthesize)
    {
        public static Piece Of(string text)
        {
            return new Piece(text, null, null, false);
        }

        public static Piece Expand(OwlClassExpression expression, bool parenthesize)
        {
            return new Piece(null, expression, null, parenthesize);
        }

        public static Piece Expand(OwlDataRange range, bool parenthesize)
        {
            return new Piece(null, null, range, parenthesize);
        }
    }

    private static string RenderClass(OwlClassExpression expression, bool parenthesize)
    {
        StringBuilder text = new();
        Stack<Piece> work = new();
        work.Push(Piece.Expand(expression, parenthesize));
        Drain(text, work);

        return text.ToString();
    }

    private static string RenderDataRange(OwlDataRange range, bool parenthesize)
    {
        StringBuilder text = new();
        Stack<Piece> work = new();
        work.Push(Piece.Expand(range, parenthesize));
        Drain(text, work);

        return text.ToString();
    }

    private static void Drain(StringBuilder text, Stack<Piece> work)
    {
        while(work.Count > 0)
        {
            Piece piece = work.Pop();
            if(piece.Text is string literal)
            {
                text.Append(literal);

                continue;
            }

            if(piece.Class is OwlClassExpression expression)
            {
                ExpandClass(text, work, expression, piece.Parenthesize);

                continue;
            }

            ExpandDataRange(text, work, piece.Range!, piece.Parenthesize);
        }
    }

    /// <summary>Whether a class expression stands alone without parentheses: a named class or an enumeration.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns><see langword="true"/> for atomics.</returns>
    private static bool IsAtomic(OwlClassExpression expression)
    {
        return expression is OwlClassReference or OwlObjectOneOf;
    }

    /// <summary>Whether a data range stands alone without parentheses: a datatype, an enumeration, or a facet restriction.</summary>
    /// <param name="range">The range.</param>
    /// <returns><see langword="true"/> for atomics.</returns>
    private static bool IsAtomic(OwlDataRange range)
    {
        return range is OwlDatatypeReference or OwlDataOneOf or OwlDatatypeRestriction;
    }

    private static void ExpandClass(StringBuilder text, Stack<Piece> work, OwlClassExpression expression, bool parenthesize)
    {
        bool wrap = parenthesize && !IsAtomic(expression);
        List<Piece> pieces = [];
        if(wrap)
        {
            pieces.Add(Piece.Of("("));
        }

        switch(expression)
        {
            case OwlClassReference reference:
            {
                pieces.Add(Piece.Of($"<{reference.Class.Iri}>"));
                break;
            }

            case OwlObjectOneOf oneOf:
            {
                StringBuilder set = new();
                set.Append('{');
                for(int i = 0; i < oneOf.Individuals.Count; i++)
                {
                    if(i > 0)
                    {
                        set.Append(", ");
                    }

                    AppendTerm(set, oneOf.Individuals[i]);
                }

                set.Append('}');
                pieces.Add(Piece.Of(set.ToString()));
                break;
            }

            case OwlObjectIntersectionOf intersection:
            {
                for(int i = 0; i < intersection.Operands.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(" and "));
                    }

                    pieces.Add(Piece.Expand(intersection.Operands[i], parenthesize: true));
                }

                break;
            }

            case OwlObjectUnionOf union:
            {
                for(int i = 0; i < union.Operands.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(" or "));
                    }

                    pieces.Add(Piece.Expand(union.Operands[i], parenthesize: true));
                }

                break;
            }

            case OwlObjectComplementOf complement:
            {
                pieces.Add(Piece.Of("not "));
                pieces.Add(Piece.Expand(complement.Operand, parenthesize: true));
                break;
            }

            case OwlObjectSomeValuesFrom someValues:
            {
                pieces.Add(Piece.Of($"{RenderObjectProperty(someValues.Property)} some "));
                pieces.Add(Piece.Expand(someValues.Filler, parenthesize: true));
                break;
            }

            case OwlObjectAllValuesFrom allValues:
            {
                pieces.Add(Piece.Of($"{RenderObjectProperty(allValues.Property)} only "));
                pieces.Add(Piece.Expand(allValues.Filler, parenthesize: true));
                break;
            }

            case OwlObjectHasValue hasValue:
            {
                pieces.Add(Piece.Of($"{RenderObjectProperty(hasValue.Property)} value {RenderTerm(hasValue.Individual)}"));
                break;
            }

            case OwlObjectHasSelf hasSelf:
            {
                pieces.Add(Piece.Of($"{RenderObjectProperty(hasSelf.Property)} Self"));
                break;
            }

            case OwlObjectCardinality cardinality:
            {
                string word = cardinality.Kind switch
                {
                    OwlCardinalityKind.Min => "min",
                    OwlCardinalityKind.Max => "max",
                    _ => "exactly"
                };

                pieces.Add(Piece.Of($"{RenderObjectProperty(cardinality.Property)} {word} {cardinality.Cardinality.ToString(CultureInfo.InvariantCulture)}"));
                if(cardinality.Filler is OwlClassExpression filler)
                {
                    pieces.Add(Piece.Of(" "));
                    pieces.Add(Piece.Expand(filler, parenthesize: true));
                }

                break;
            }

            case OwlDataSomeValuesFrom dataSome when dataSome.Properties.Count == 1:
            {
                pieces.Add(Piece.Of($"<{dataSome.Properties[0].Iri}> some "));
                pieces.Add(Piece.Expand(dataSome.Range, parenthesize: true));
                break;
            }

            case OwlDataAllValuesFrom dataAll when dataAll.Properties.Count == 1:
            {
                pieces.Add(Piece.Of($"<{dataAll.Properties[0].Iri}> only "));
                pieces.Add(Piece.Expand(dataAll.Range, parenthesize: true));
                break;
            }

            case OwlDataHasValue dataHasValue:
            {
                pieces.Add(Piece.Of($"<{dataHasValue.Property.Iri}> value {RenderTerm(dataHasValue.Value)}"));
                break;
            }

            case OwlDataCardinality dataCardinality:
            {
                string word = dataCardinality.Kind switch
                {
                    OwlCardinalityKind.Min => "min",
                    OwlCardinalityKind.Max => "max",
                    _ => "exactly"
                };

                pieces.Add(Piece.Of($"<{dataCardinality.Property.Iri}> {word} {dataCardinality.Cardinality.ToString(CultureInfo.InvariantCulture)}"));
                if(dataCardinality.Range is OwlDataRange qualifier)
                {
                    pieces.Add(Piece.Of(" "));
                    pieces.Add(Piece.Expand(qualifier, parenthesize: true));
                }

                break;
            }

            default:
            {
                //An n-ary data quantifier over several properties has no
                //Manchester form; an invalid marker keeps the text readable.
                pieces.Add(Piece.Of("<urn:veritas:inexpressible>"));
                break;
            }
        }

        if(wrap)
        {
            pieces.Add(Piece.Of(")"));
        }

        for(int i = pieces.Count - 1; i >= 0; i--)
        {
            work.Push(pieces[i]);
        }
    }

    private static void ExpandDataRange(StringBuilder text, Stack<Piece> work, OwlDataRange range, bool parenthesize)
    {
        bool wrap = parenthesize && !IsAtomic(range);
        List<Piece> pieces = [];
        if(wrap)
        {
            pieces.Add(Piece.Of("("));
        }

        switch(range)
        {
            case OwlDatatypeReference reference:
            {
                pieces.Add(Piece.Of($"<{reference.Datatype.Iri}>"));
                break;
            }

            case OwlDataOneOf oneOf:
            {
                StringBuilder set = new();
                set.Append('{');
                for(int i = 0; i < oneOf.Literals.Count; i++)
                {
                    if(i > 0)
                    {
                        set.Append(", ");
                    }

                    AppendLiteral(set, oneOf.Literals[i]);
                }

                set.Append('}');
                pieces.Add(Piece.Of(set.ToString()));
                break;
            }

            case OwlDatatypeRestriction restriction:
            {
                StringBuilder facets = new();
                facets.Append('<').Append(restriction.Datatype.Iri.ToString()).Append(">[");
                for(int i = 0; i < restriction.Restrictions.Count; i++)
                {
                    if(i > 0)
                    {
                        facets.Append(", ");
                    }

                    facets.Append(FacetWord(restriction.Restrictions[i].Facet)).Append(' ');
                    AppendLiteral(facets, restriction.Restrictions[i].Value);
                }

                facets.Append(']');
                pieces.Add(Piece.Of(facets.ToString()));
                break;
            }

            case OwlDataIntersectionOf intersection:
            {
                for(int i = 0; i < intersection.Ranges.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(" and "));
                    }

                    pieces.Add(Piece.Expand(intersection.Ranges[i], parenthesize: true));
                }

                break;
            }

            case OwlDataUnionOf union:
            {
                for(int i = 0; i < union.Ranges.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(" or "));
                    }

                    pieces.Add(Piece.Expand(union.Ranges[i], parenthesize: true));
                }

                break;
            }

            case OwlDataComplementOf complement:
            {
                pieces.Add(Piece.Of("not "));
                pieces.Add(Piece.Expand(complement.Range, parenthesize: true));
                break;
            }

            default:
            {
                pieces.Add(Piece.Of("<urn:veritas:inexpressible>"));
                break;
            }
        }

        if(wrap)
        {
            pieces.Add(Piece.Of(")"));
        }

        for(int i = pieces.Count - 1; i >= 0; i--)
        {
            work.Push(pieces[i]);
        }
    }

    /// <summary>Maps a constraining-facet IRI back to its Manchester word or comparison operator.</summary>
    /// <param name="facet">The facet IRI node.</param>
    /// <returns>The facet token.</returns>
    private static string FacetWord(NamedNode facet)
    {
        string iri = facet.Iri.ToString();
        foreach(KeyValuePair<string, string> named in OwlManchesterWords.NamedFacets)
        {
            if(named.Value == iri)
            {
                return named.Key;
            }
        }

        foreach(KeyValuePair<string, string> comparison in OwlManchesterWords.ComparisonFacets)
        {
            if(comparison.Value == iri)
            {
                return comparison.Key;
            }
        }

        //An unknown facet IRI has no Manchester abbreviation; the raw IRI at
        //least reads back as an unknown facet rather than vanishing.
        return iri;
    }
}
