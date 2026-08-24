using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// The rendering is UTF-8 byte-native and deterministic and prefix-free: every
/// IRI is written in full angle-bracket form; axioms group into frames in order
/// of first appearance; each section line carries one axiom, its annotations
/// leading the payload, so the annotated-list grammar stays unambiguous. Reading
/// a rendering and rendering the result reproduces the same text.
/// </para>
/// <para>
/// Keywords and punctuation are emitted directly from their <c>u8</c> byte
/// sequences in <see cref="OwlManchesterWords"/> and term values from their
/// <see cref="Utf8String"/> bytes, so no UTF-16 intermediate is ever
/// materialised. Numbers format through <see cref="Utf8Formatter"/> and literal
/// escaping scans the bytes.
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
    /// Renders the document as UTF-8 Manchester-syntax bytes into a caller-supplied writer.
    /// </summary>
    /// <param name="document">The structural document.</param>
    /// <param name="diagnostics">The bag inexpressible-axiom warnings record into.</param>
    /// <param name="output">The destination buffer writer the caller owns and threads.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void Write(OwlOntologyDocument document, DiagnosticBag diagnostics, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(output);

        Plan plan = new(document.OntologyIri, diagnostics);

        foreach(OwlAxiom axiom in document.Axioms)
        {
            PlaceAxiom(plan, axiom);
        }

        plan.PlacePendingAnnotations();
        plan.Render(output);
    }

    /// <summary>
    /// Renders the document to Manchester-syntax text, decoding the UTF-8 bytes once at the boundary.
    /// </summary>
    /// <param name="document">The structural document.</param>
    /// <param name="diagnostics">The bag inexpressible-axiom warnings record into.</param>
    /// <returns>The Manchester-syntax text.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Write(OwlOntologyDocument document, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ArrayBufferWriter<byte> buffer = new();
        Write(document, diagnostics, buffer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    //One frame section line: optional annotations and a rendered payload.
    private sealed record SectionLine(ImmutableAnnotations Annotations, Utf8String Payload);

    //The annotations of one line, kept as the structural values until rendering.
    private sealed record ImmutableAnnotations(ImmutableArray<OwlAnnotation> Values);

    //One frame: its keyword bytes, its subject text, and its section lines in order.
    private sealed class Frame
    {
        /// <summary>The frame keyword, such as <c>Class:</c>, as its byte sequence.</summary>
        public required Utf8String Keyword { get; init; }

        /// <summary>The rendered subject term.</summary>
        public required Utf8String Subject { get; init; }

        /// <summary>The section lines, in placement order.</summary>
        public List<FrameLine> Lines { get; } = [];
    }

    //One placed frame line: its section keyword bytes and the line payload.
    private readonly record struct FrameLine(Utf8String Section, SectionLine Line);

    //One placed entity-free n-ary line: its keyword bytes and the line payload.
    private readonly record struct MiscLine(Utf8String Keyword, SectionLine Line);

    //The frame key, as the keyword and subject byte sequences in first-appearance order.
    private readonly record struct FrameKey(Utf8String Keyword, Utf8String Subject);

    //The document plan: header, frames in first-appearance order, misc lines.
    private sealed class Plan
    {
        /// <summary>Initialises the plan for a document.</summary>
        /// <param name="ontologyIri">The ontology IRI, when the document names one.</param>
        /// <param name="diagnostics">The bag inexpressibility warnings record into.</param>
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
        public List<Utf8String> Imports { get; } = [];

        /// <summary>The ontology-level annotations, in document order.</summary>
        public List<OwlAnnotation> OntologyAnnotations { get; } = [];

        /// <summary>The frames, keyed by keyword and subject, in first-appearance order.</summary>
        public Dictionary<FrameKey, Frame> Frames { get; } = [];

        /// <summary>The frame keys in first-appearance order.</summary>
        public List<FrameKey> FrameOrder { get; } = [];

        /// <summary>The entity-free n-ary lines, in document order.</summary>
        public List<MiscLine> MiscLines { get; } = [];

        /// <summary>Annotation assertions awaiting a frame to host them, in document order.</summary>
        public List<OwlAnnotationAssertionAxiom> PendingAnnotations { get; } = [];

        /// <summary>Finds or creates the frame for a subject.</summary>
        /// <param name="keyword">The frame keyword bytes.</param>
        /// <param name="subject">The rendered subject.</param>
        /// <returns>The frame.</returns>
        public Frame EnsureFrame(ReadOnlySpan<byte> keyword, Utf8String subject)
        {
            FrameKey key = new(new Utf8String(keyword.ToArray()), subject);
            if(!Frames.TryGetValue(key, out Frame? frame))
            {
                frame = new Frame { Keyword = key.Keyword, Subject = subject };
                Frames[key] = frame;
                FrameOrder.Add(key);
            }

            return frame;
        }

        /// <summary>Adds one section line to a frame, unless the payload contains an inexpressible sub-expression.</summary>
        /// <param name="keyword">The frame keyword bytes.</param>
        /// <param name="subject">The rendered subject.</param>
        /// <param name="section">The section keyword bytes.</param>
        /// <param name="axiom">The axiom whose annotations lead the line.</param>
        /// <param name="payload">The rendered payload.</param>
        /// <param name="inexpressible">Whether the payload met a shape with no Manchester form.</param>
        public void AddLine(ReadOnlySpan<byte> keyword, Utf8String subject, ReadOnlySpan<byte> section, OwlAxiom axiom, Utf8String payload, bool inexpressible)
        {
            //An expression renderer that met an n-ary data quantifier (or
            //another shape with no Manchester form) raised the flag; the whole
            //axiom is skipped rather than silently altered.
            if(inexpressible)
            {
                Skip("an expression with no Manchester form");

                return;
            }

            EnsureFrame(keyword, subject).Lines.Add(new FrameLine(new Utf8String(section.ToArray()), new SectionLine(new ImmutableAnnotations(axiom.Annotations), payload)));
        }

        /// <summary>Records an inexpressible axiom and skips it.</summary>
        /// <param name="reason">What the syntax cannot express.</param>
        public void Skip(string reason)
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.UnsupportedConstruct,
                DiagnosticSeverity.Warning,
                default,
                Utf8Strings.From($"Manchester syntax cannot express this axiom: {reason}")));
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

                Utf8String subject = RenderTerm(axiom.Subject);
                Frame? host = null;
                foreach(FrameKey key in FrameOrder)
                {
                    if(key.Subject.Equals(subject))
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

                host.Lines.Add(new FrameLine(
                    new Utf8String(OwlManchesterWords.AnnotationsKeyword.ToArray()),
                    new SectionLine(
                        new ImmutableAnnotations(axiom.Annotations),
                        JoinSpaced(RenderTerm(axiom.Property), RenderTerm(axiom.Value)))));
            }
        }

        /// <summary>Renders the plan to the document bytes.</summary>
        /// <param name="output">The destination buffer writer.</param>
        public void Render(IBufferWriter<byte> output)
        {
            output.Write(OwlManchesterWords.OntologyKeyword);
            if(OntologyIri is NamedNode iri)
            {
                output.Write(" <"u8);
                output.Write(iri.Iri.Span);
                output.Write(">"u8);
            }

            output.Write("\n"u8);

            foreach(Utf8String import in Imports)
            {
                output.Write(OwlManchesterWords.ImportKeyword);
                output.Write(" "u8);
                output.Write(import.Span);
                output.Write("\n"u8);
            }

            foreach(OwlAnnotation annotation in OntologyAnnotations)
            {
                output.Write(OwlManchesterWords.AnnotationsKeyword);
                output.Write(" "u8);
                WriteAnnotation(output, annotation);
                output.Write("\n"u8);
            }

            foreach(FrameKey key in FrameOrder)
            {
                Frame frame = Frames[key];
                output.Write("\n"u8);
                output.Write(frame.Keyword.Span);
                output.Write(" "u8);
                output.Write(frame.Subject.Span);
                output.Write("\n"u8);

                foreach(FrameLine line in frame.Lines)
                {
                    output.Write("    "u8);
                    output.Write(line.Section.Span);
                    output.Write(" "u8);
                    WriteLineAnnotations(output, line.Line.Annotations);
                    output.Write(line.Line.Payload.Span);
                    output.Write("\n"u8);
                }
            }

            foreach(MiscLine misc in MiscLines)
            {
                output.Write("\n"u8);
                output.Write(misc.Keyword.Span);
                output.Write(" "u8);
                WriteLineAnnotations(output, misc.Line.Annotations);
                output.Write(misc.Line.Payload.Span);
                output.Write("\n"u8);
            }
        }

        /// <summary>Writes a line's leading <c>Annotations:</c> block, when it has one.</summary>
        /// <param name="output">The destination buffer writer.</param>
        /// <param name="annotations">The line's annotations.</param>
        private static void WriteLineAnnotations(IBufferWriter<byte> output, ImmutableAnnotations annotations)
        {
            if(annotations.Values.IsDefaultOrEmpty)
            {
                return;
            }

            output.Write(OwlManchesterWords.AnnotationsKeyword);
            output.Write(" "u8);
            for(int i = 0; i < annotations.Values.Length; i++)
            {
                if(i > 0)
                {
                    output.Write(", "u8);
                }

                WriteAnnotation(output, annotations.Values[i]);
            }

            output.Write(" "u8);
        }

        /// <summary>Writes one annotation: its nested block, its property, and its target.</summary>
        /// <param name="output">The destination buffer writer.</param>
        /// <param name="annotation">The annotation.</param>
        private static void WriteAnnotation(IBufferWriter<byte> output, OwlAnnotation annotation)
        {
            //Nested annotation blocks recurse structurally; an explicit stack
            //of pending pieces keeps the call stack flat.
            Stack<AnnotationPiece> work = new();
            work.Push(AnnotationPiece.Of(annotation));

            while(work.Count > 0)
            {
                AnnotationPiece piece = work.Pop();
                if(piece.Text is Utf8String literal)
                {
                    output.Write(literal.Span);

                    continue;
                }

                OwlAnnotation current = piece.Annotation!;
                work.Push(AnnotationPiece.Of(JoinSpaced(RenderTerm(current.Property), RenderTerm(current.Value))));

                for(int i = current.Annotations.Length - 1; i >= 0; i--)
                {
                    work.Push(AnnotationPiece.Of(new Utf8String(" "u8.ToArray())));
                    work.Push(AnnotationPiece.Of(current.Annotations[i]));
                    if(i == 0)
                    {
                        //The stack pops last-in-first-out, so the trailing space is pushed
                        //before the keyword to make the keyword pop first: "Annotations: ".
                        work.Push(AnnotationPiece.Of(new Utf8String(" "u8.ToArray())));
                        work.Push(AnnotationPiece.Of(new Utf8String(OwlManchesterWords.AnnotationsKeyword.ToArray())));
                    }
                    else
                    {
                        //Continuing annotations join the same nested block.
                        work.Push(AnnotationPiece.Of(new Utf8String(", "u8.ToArray())));
                    }
                }
            }
        }
    }

    //One unit of annotation-rendering work: literal bytes to emit, or an
    //annotation to expand.
    private readonly record struct AnnotationPiece(Utf8String? Text, OwlAnnotation? Annotation)
    {
        /// <summary>A literal-text piece.</summary>
        /// <param name="text">The bytes to emit verbatim.</param>
        /// <returns>The piece.</returns>
        public static AnnotationPiece Of(Utf8String text)
        {
            return new AnnotationPiece(text, null);
        }

        /// <summary>An annotation-to-expand piece.</summary>
        /// <param name="annotation">The annotation.</param>
        /// <returns>The piece.</returns>
        public static AnnotationPiece Of(OwlAnnotation annotation)
        {
            return new AnnotationPiece(null, annotation);
        }
    }

    //Axiom placement: each axiom either lands as one frame section line, a
    //misc line, a header entry, or a recorded skip.
    private static void PlaceAxiom(Plan plan, OwlAxiom axiom)
    {
        switch(axiom)
        {
            case(OwlImportAxiom import):
            {
                plan.Imports.Add(RenderIri(import.Imported.Iri));
                break;
            }
            case(OwlDeclarationAxiom declaration):
            {
                if(!declaration.Annotations.IsDefaultOrEmpty)
                {
                    plan.Skip("an annotated declaration");
                    break;
                }

                plan.EnsureFrame(FrameKeyword(declaration.Kind), RenderIri(declaration.Entity.Iri));
                break;
            }
            case(OwlAnnotationAssertionAxiom annotation):
            {
                plan.PendingAnnotations.Add(annotation);
                break;
            }
            case(OwlSubClassOfAxiom subClass):
            {
                if(subClass.SubClass is not OwlClassReference reference)
                {
                    plan.Skip("a general class inclusion whose subclass is anonymous");
                    break;
                }

                Utf8String payload = RenderClass(subClass.SuperClass, parenthesize: false, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.ClassFrame, RenderTerm(reference.Class), OwlManchesterWords.SubClassOfSection, axiom, payload, inexpressible);
                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                if(equivalent.First is OwlClassReference firstReference)
                {
                    Utf8String payload = RenderClass(equivalent.Second, parenthesize: false, out bool inexpressible);
                    plan.AddLine(OwlManchesterWords.ClassFrame, RenderTerm(firstReference.Class), OwlManchesterWords.EquivalentToSection, axiom, payload, inexpressible);
                }
                else if(equivalent.Second is OwlClassReference secondReference)
                {
                    Utf8String payload = RenderClass(equivalent.First, parenthesize: false, out bool inexpressible);
                    plan.AddLine(OwlManchesterWords.ClassFrame, RenderTerm(secondReference.Class), OwlManchesterWords.EquivalentToSection, axiom, payload, inexpressible);
                }
                else
                {
                    Utf8String first = RenderClass(equivalent.First, parenthesize: false, out _);
                    Utf8String second = RenderClass(equivalent.Second, parenthesize: false, out _);
                    AddMiscLine(plan, OwlManchesterWords.EquivalentClassesFrame, axiom, JoinList(first, second));
                }

                break;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                if(disjoint.Operands.Count == 2 && disjoint.Operands[0] is OwlClassReference firstReference)
                {
                    Utf8String payload = RenderClass(disjoint.Operands[1], parenthesize: false, out bool inexpressible);
                    plan.AddLine(OwlManchesterWords.ClassFrame, RenderTerm(firstReference.Class), OwlManchesterWords.DisjointWithSection, axiom, payload, inexpressible);
                    break;
                }

                Utf8String joined = JoinClasses(disjoint.Operands, out _);
                AddMiscLine(plan, OwlManchesterWords.DisjointClassesFrame, axiom, joined);
                break;
            }
            case(OwlDisjointUnionAxiom disjointUnion):
            {
                Utf8String joined = JoinClasses(disjointUnion.Operands, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.ClassFrame, RenderIri(disjointUnion.Class.Iri), OwlManchesterWords.DisjointUnionOfSection, axiom, joined, inexpressible);
                break;
            }
            case(OwlHasKeyAxiom hasKey):
            {
                if(hasKey.Class is not OwlClassReference reference)
                {
                    plan.Skip("a key on an anonymous class expression");
                    break;
                }

                ArrayBufferWriter<byte> keys = new();
                bool firstKey = true;
                foreach(OwlObjectPropertyExpression objectKey in hasKey.ObjectProperties)
                {
                    AppendSeparated(keys, " "u8, ref firstKey);
                    keys.Write(RenderObjectProperty(objectKey).Span);
                }

                foreach(NamedNode dataKey in hasKey.DataProperties)
                {
                    AppendSeparated(keys, " "u8, ref firstKey);
                    keys.Write(RenderIri(dataKey.Iri).Span);
                }

                plan.AddLine(OwlManchesterWords.ClassFrame, RenderTerm(reference.Class), OwlManchesterWords.HasKeySection, axiom, ToUtf8String(keys), inexpressible: false);
                break;
            }
            case(OwlPropertyChainAxiom chain):
            {
                if(chain.SuperProperty is not OwlObjectPropertyReference superReference)
                {
                    plan.Skip("a property chain whose super property is an inverse expression");
                    break;
                }

                ArrayBufferWriter<byte> links = new();
                bool firstLink = true;
                foreach(OwlObjectPropertyExpression link in chain.Chain)
                {
                    AppendSeparated(links, " o "u8, ref firstLink);
                    links.Write(RenderObjectProperty(link).Span);
                }

                plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(superReference.Named), OwlManchesterWords.SubPropertyChainSection, axiom, ToUtf8String(links), inexpressible: false);
                break;
            }
            case(OwlSubObjectPropertyOfAxiom subProperty):
            {
                if(subProperty.SubProperty is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a sub-property axiom whose subproperty is an inverse expression");
                    break;
                }

                plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(reference.Named), OwlManchesterWords.SubPropertyOfSection, axiom, RenderObjectProperty(subProperty.SuperProperty), inexpressible: false);
                break;
            }
            case(OwlEquivalentObjectPropertiesAxiom equivalentProperties):
            {
                if(equivalentProperties.First is OwlObjectPropertyReference firstReference)
                {
                    plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(firstReference.Named), OwlManchesterWords.EquivalentToSection, axiom, RenderObjectProperty(equivalentProperties.Second), inexpressible: false);
                }
                else if(equivalentProperties.Second is OwlObjectPropertyReference secondReference)
                {
                    plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(secondReference.Named), OwlManchesterWords.EquivalentToSection, axiom, RenderObjectProperty(equivalentProperties.First), inexpressible: false);
                }
                else
                {
                    plan.Skip("a property equivalence between two inverse expressions");
                }

                break;
            }
            case(OwlDisjointObjectPropertiesAxiom disjointProperties):
            {
                if(disjointProperties.Operands.Count == 2 && disjointProperties.Operands[0] is OwlObjectPropertyReference firstReference)
                {
                    plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(firstReference.Named), OwlManchesterWords.DisjointWithSection, axiom, RenderObjectProperty(disjointProperties.Operands[1]), inexpressible: false);
                    break;
                }

                ArrayBufferWriter<byte> properties = new();
                bool firstProperty = true;
                bool expressible = true;
                foreach(OwlObjectPropertyExpression operand in disjointProperties.Operands)
                {
                    if(operand is not OwlObjectPropertyReference operandReference)
                    {
                        expressible = false;
                        break;
                    }

                    AppendSeparated(properties, ", "u8, ref firstProperty);
                    properties.Write(RenderTerm(operandReference.Named).Span);
                }

                if(!expressible)
                {
                    plan.Skip("a property disjointness over inverse expressions");
                    break;
                }

                AddMiscLine(plan, OwlManchesterWords.DisjointPropertiesFrame, axiom, ToUtf8String(properties));
                break;
            }
            case(OwlInverseObjectPropertiesAxiom inverse):
            {
                if(inverse.First is OwlObjectPropertyReference firstReference)
                {
                    plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(firstReference.Named), OwlManchesterWords.InverseOfSection, axiom, RenderObjectProperty(inverse.Second), inexpressible: false);
                }
                else if(inverse.Second is OwlObjectPropertyReference secondReference)
                {
                    plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(secondReference.Named), OwlManchesterWords.InverseOfSection, axiom, RenderObjectProperty(inverse.First), inexpressible: false);
                }
                else
                {
                    plan.Skip("an inverse-properties axiom between two inverse expressions");
                }

                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                if(domain.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a domain on an inverse property expression");
                    break;
                }

                Utf8String payload = RenderClass(domain.Domain, parenthesize: false, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(reference.Named), OwlManchesterWords.DomainSection, axiom, payload, inexpressible);
                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                if(range.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a range on an inverse property expression");
                    break;
                }

                Utf8String payload = RenderClass(range.Range, parenthesize: false, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(reference.Named), OwlManchesterWords.RangeSection, axiom, payload, inexpressible);
                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                if(characteristic.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a characteristic on an inverse property expression");
                    break;
                }

                plan.AddLine(OwlManchesterWords.ObjectPropertyFrame, RenderTerm(reference.Named), OwlManchesterWords.CharacteristicsSection, axiom, new Utf8String(CharacteristicWord(characteristic.Characteristic).ToArray()), inexpressible: false);
                break;
            }
            case(OwlSubDataPropertyOfAxiom subData):
            {
                plan.AddLine(OwlManchesterWords.DataPropertyFrame, RenderIri(subData.SubProperty.Iri), OwlManchesterWords.SubPropertyOfSection, axiom, RenderIri(subData.SuperProperty.Iri), inexpressible: false);
                break;
            }
            case(OwlEquivalentDataPropertiesAxiom equivalentData):
            {
                plan.AddLine(OwlManchesterWords.DataPropertyFrame, RenderIri(equivalentData.First.Iri), OwlManchesterWords.EquivalentToSection, axiom, RenderIri(equivalentData.Second.Iri), inexpressible: false);
                break;
            }
            case(OwlDisjointDataPropertiesAxiom disjointData):
            {
                if(disjointData.Operands.Count == 2)
                {
                    plan.AddLine(OwlManchesterWords.DataPropertyFrame, RenderIri(disjointData.Operands[0].Iri), OwlManchesterWords.DisjointWithSection, axiom, RenderIri(disjointData.Operands[1].Iri), inexpressible: false);
                    break;
                }

                ArrayBufferWriter<byte> properties = new();
                bool firstProperty = true;
                foreach(NamedNode operand in disjointData.Operands)
                {
                    AppendSeparated(properties, ", "u8, ref firstProperty);
                    properties.Write(RenderIri(operand.Iri).Span);
                }

                AddMiscLine(plan, OwlManchesterWords.DisjointPropertiesFrame, axiom, ToUtf8String(properties));
                break;
            }
            case(OwlDataPropertyDomainAxiom dataDomain):
            {
                Utf8String payload = RenderClass(dataDomain.Domain, parenthesize: false, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.DataPropertyFrame, RenderIri(dataDomain.Property.Iri), OwlManchesterWords.DomainSection, axiom, payload, inexpressible);
                break;
            }
            case(OwlDataPropertyRangeAxiom dataRange):
            {
                Utf8String payload = RenderDataRange(dataRange.Range, parenthesize: false, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.DataPropertyFrame, RenderIri(dataRange.Property.Iri), OwlManchesterWords.RangeSection, axiom, payload, inexpressible);
                break;
            }
            case(OwlFunctionalDataPropertyAxiom functionalData):
            {
                plan.AddLine(OwlManchesterWords.DataPropertyFrame, RenderIri(functionalData.Property.Iri), OwlManchesterWords.CharacteristicsSection, axiom, new Utf8String(OwlManchesterWords.FunctionalWord.ToArray()), inexpressible: false);
                break;
            }
            case(OwlDatatypeDefinitionAxiom definition):
            {
                Utf8String payload = RenderDataRange(definition.Range, parenthesize: false, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.DatatypeFrame, RenderIri(definition.Datatype.Iri), OwlManchesterWords.EquivalentToSection, axiom, payload, inexpressible);
                break;
            }
            case(OwlSubAnnotationPropertyOfAxiom subAnnotation):
            {
                plan.AddLine(OwlManchesterWords.AnnotationPropertyFrame, RenderIri(subAnnotation.SubProperty.Iri), OwlManchesterWords.SubPropertyOfSection, axiom, RenderIri(subAnnotation.SuperProperty.Iri), inexpressible: false);
                break;
            }
            case(OwlAnnotationPropertyDomainAxiom annotationDomain):
            {
                plan.AddLine(OwlManchesterWords.AnnotationPropertyFrame, RenderIri(annotationDomain.Property.Iri), OwlManchesterWords.DomainSection, axiom, RenderIri(annotationDomain.Domain.Iri), inexpressible: false);
                break;
            }
            case(OwlAnnotationPropertyRangeAxiom annotationRange):
            {
                plan.AddLine(OwlManchesterWords.AnnotationPropertyFrame, RenderIri(annotationRange.Property.Iri), OwlManchesterWords.RangeSection, axiom, RenderIri(annotationRange.Range.Iri), inexpressible: false);
                break;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                Utf8String payload = RenderClass(assertion.Class, parenthesize: false, out bool inexpressible);
                plan.AddLine(OwlManchesterWords.IndividualFrame, RenderTerm(assertion.Individual), OwlManchesterWords.TypesSection, axiom, payload, inexpressible);
                break;
            }
            case(OwlObjectPropertyAssertionAxiom objectAssertion):
            {
                Utf8String payload = JoinSpaced(RenderIri(objectAssertion.Property.Iri), RenderTerm(objectAssertion.Target));
                plan.AddLine(OwlManchesterWords.IndividualFrame, RenderTerm(objectAssertion.Source), OwlManchesterWords.FactsSection, axiom, payload, inexpressible: false);
                break;
            }
            case(OwlNegativeObjectPropertyAssertionAxiom negativeObject):
            {
                if(negativeObject.Property is not OwlObjectPropertyReference reference)
                {
                    plan.Skip("a negative assertion on an inverse property expression");
                    break;
                }

                Utf8String payload = JoinNegated(RenderIri(reference.Named.Iri), RenderTerm(negativeObject.Target));
                plan.AddLine(OwlManchesterWords.IndividualFrame, RenderTerm(negativeObject.Source), OwlManchesterWords.FactsSection, axiom, payload, inexpressible: false);
                break;
            }
            case(OwlDataPropertyAssertionAxiom dataAssertion):
            {
                Utf8String payload = JoinSpaced(RenderIri(dataAssertion.Property.Iri), RenderTerm(dataAssertion.Target));
                plan.AddLine(OwlManchesterWords.IndividualFrame, RenderTerm(dataAssertion.Source), OwlManchesterWords.FactsSection, axiom, payload, inexpressible: false);
                break;
            }
            case(OwlNegativeDataPropertyAssertionAxiom negativeData):
            {
                Utf8String payload = JoinNegated(RenderIri(negativeData.Property.Iri), RenderTerm(negativeData.Target));
                plan.AddLine(OwlManchesterWords.IndividualFrame, RenderTerm(negativeData.Source), OwlManchesterWords.FactsSection, axiom, payload, inexpressible: false);
                break;
            }
            case(OwlSameIndividualAxiom same):
            {
                plan.AddLine(OwlManchesterWords.IndividualFrame, RenderTerm(same.First), OwlManchesterWords.SameAsSection, axiom, RenderTerm(same.Second), inexpressible: false);
                break;
            }
            case(OwlDifferentIndividualsAxiom different):
            {
                if(different.Individuals.Count == 2)
                {
                    plan.AddLine(OwlManchesterWords.IndividualFrame, RenderTerm(different.Individuals[0]), OwlManchesterWords.DifferentFromSection, axiom, RenderTerm(different.Individuals[1]), inexpressible: false);
                    break;
                }

                ArrayBufferWriter<byte> individuals = new();
                bool firstIndividual = true;
                foreach(RdfTerm individual in different.Individuals)
                {
                    AppendSeparated(individuals, ", "u8, ref firstIndividual);
                    individuals.Write(RenderTerm(individual).Span);
                }

                AddMiscLine(plan, OwlManchesterWords.DifferentIndividualsFrame, axiom, ToUtf8String(individuals));
                break;
            }
            default:
            {
                plan.Skip($"an axiom of kind {axiom.GetType().Name}");
                break;
            }
        }
    }

    /// <summary>Adds an entity-free n-ary misc line, carrying any inexpressibility marker through in the payload.</summary>
    /// <param name="plan">The document plan.</param>
    /// <param name="keyword">The misc frame keyword bytes.</param>
    /// <param name="axiom">The axiom whose annotations lead the line.</param>
    /// <param name="payload">The rendered payload.</param>
    private static void AddMiscLine(Plan plan, ReadOnlySpan<byte> keyword, OwlAxiom axiom, Utf8String payload)
    {

        plan.MiscLines.Add(new MiscLine(new Utf8String(keyword.ToArray()), new SectionLine(new ImmutableAnnotations(axiom.Annotations), payload)));
    }

    /// <summary>The entity-frame keyword bytes for a declaration kind.</summary>
    /// <param name="kind">The entity kind.</param>
    /// <returns>The keyword bytes.</returns>
    private static ReadOnlySpan<byte> FrameKeyword(OwlEntityKind kind)
    {
        return kind switch
        {
            OwlEntityKind.Class => OwlManchesterWords.ClassFrame,
            OwlEntityKind.Datatype => OwlManchesterWords.DatatypeFrame,
            OwlEntityKind.ObjectProperty => OwlManchesterWords.ObjectPropertyFrame,
            OwlEntityKind.DataProperty => OwlManchesterWords.DataPropertyFrame,
            OwlEntityKind.AnnotationProperty => OwlManchesterWords.AnnotationPropertyFrame,
            _ => OwlManchesterWords.IndividualFrame
        };
    }

    /// <summary>The characteristic-word bytes for an object-property characteristic.</summary>
    /// <param name="characteristic">The characteristic.</param>
    /// <returns>The word bytes.</returns>
    private static ReadOnlySpan<byte> CharacteristicWord(OwlPropertyCharacteristic characteristic)
    {
        return characteristic switch
        {
            OwlPropertyCharacteristic.Functional => OwlManchesterWords.FunctionalWord,
            OwlPropertyCharacteristic.InverseFunctional => OwlManchesterWords.InverseFunctionalWord,
            OwlPropertyCharacteristic.Transitive => OwlManchesterWords.TransitiveWord,
            OwlPropertyCharacteristic.Symmetric => OwlManchesterWords.SymmetricWord,
            OwlPropertyCharacteristic.Asymmetric => OwlManchesterWords.AsymmetricWord,
            OwlPropertyCharacteristic.Reflexive => OwlManchesterWords.ReflexiveWord,
            _ => OwlManchesterWords.IrreflexiveWord
        };
    }

    /// <summary>Joins rendered class expressions into a comma-separated list, flagging any inexpressible operand.</summary>
    /// <param name="expressions">The class expressions.</param>
    /// <param name="inexpressible">Set when an operand met a shape with no Manchester form.</param>
    /// <returns>The joined bytes.</returns>
    private static Utf8String JoinClasses(IReadOnlyList<OwlClassExpression> expressions, out bool inexpressible)
    {
        inexpressible = false;
        ArrayBufferWriter<byte> parts = new();
        bool first = true;
        foreach(OwlClassExpression expression in expressions)
        {
            AppendSeparated(parts, ", "u8, ref first);
            parts.Write(RenderClass(expression, parenthesize: false, out bool gap).Span);
            inexpressible |= gap;
        }

        return ToUtf8String(parts);
    }

    /// <summary>Renders an IRI in full angle-bracket form.</summary>
    /// <param name="iri">The IRI bytes.</param>
    /// <returns>The rendered term.</returns>
    private static Utf8String RenderIri(Utf8String iri)
    {
        ArrayBufferWriter<byte> text = new();
        text.Write("<"u8);
        text.Write(iri.Span);
        text.Write(">"u8);

        return ToUtf8String(text);
    }

    /// <summary>Joins two rendered fragments with a single separating space.</summary>
    /// <param name="left">The leading fragment.</param>
    /// <param name="right">The trailing fragment.</param>
    /// <returns>The joined bytes.</returns>
    private static Utf8String JoinSpaced(Utf8String left, Utf8String right)
    {
        ArrayBufferWriter<byte> text = new();
        text.Write(left.Span);
        text.Write(" "u8);
        text.Write(right.Span);

        return ToUtf8String(text);
    }

    /// <summary>Joins two rendered fragments into a comma-separated list.</summary>
    /// <param name="left">The leading fragment.</param>
    /// <param name="right">The trailing fragment.</param>
    /// <returns>The joined bytes.</returns>
    private static Utf8String JoinList(Utf8String left, Utf8String right)
    {
        ArrayBufferWriter<byte> text = new();
        text.Write(left.Span);
        text.Write(", "u8);
        text.Write(right.Span);

        return ToUtf8String(text);
    }

    /// <summary>Renders a negated fact: <c>not</c>, the property IRI, and the target.</summary>
    /// <param name="property">The rendered property IRI.</param>
    /// <param name="target">The rendered target term.</param>
    /// <returns>The joined bytes.</returns>
    private static Utf8String JoinNegated(Utf8String property, Utf8String target)
    {
        ArrayBufferWriter<byte> text = new();
        text.Write(OwlManchesterWords.NotWord);
        text.Write(" "u8);
        text.Write(property.Span);
        text.Write(" "u8);
        text.Write(target.Span);

        return ToUtf8String(text);
    }

    //Term rendering: full IRIs, blank labels, and literals in the same
    //lexical policy the functional-syntax writer uses.
    private static Utf8String RenderTerm(RdfTerm term)
    {
        ArrayBufferWriter<byte> text = new();
        AppendTerm(text, term);

        return ToUtf8String(text);
    }

    /// <summary>Appends a term's rendering: a full IRI, a blank label, or a quoted literal.</summary>
    /// <param name="text">The destination buffer writer.</param>
    /// <param name="term">The term.</param>
    private static void AppendTerm(IBufferWriter<byte> text, RdfTerm term)
    {
        switch(term)
        {
            case(NamedNode named):
            {
                text.Write("<"u8);
                text.Write(named.Iri.Span);
                text.Write(">"u8);
                break;
            }
            case(BlankNode blank):
            {
                text.Write("_:"u8);
                text.Write(blank.Label.Span);
                break;
            }
            case(Literal value):
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

    /// <summary>Appends a quoted literal with its <c>\\</c>/<c>"</c> escapes and optional language or datatype suffix.</summary>
    /// <param name="text">The destination buffer writer.</param>
    /// <param name="value">The literal.</param>
    private static void AppendLiteral(IBufferWriter<byte> text, Literal value)
    {
        text.Write("\""u8);

        ReadOnlySpan<byte> lexical = value.Value.Span;
        int start = 0;
        for(int i = 0; i < lexical.Length; i++)
        {
            if(lexical[i] == (byte)'"' || lexical[i] == (byte)'\\')
            {
                text.Write(lexical[start..i]);
                text.Write("\\"u8);
                start = i;
            }
        }

        text.Write(lexical[start..]);
        text.Write("\""u8);

        if(value.Language is Utf8String language)
        {
            text.Write("@"u8);
            text.Write(language.Span);

            return;
        }

        //A plain quoted literal reads back as xsd:string; every other
        //datatype is written explicitly.
        if(!value.Datatype.Iri.Equals(Vocabulary.Xsd.String))
        {
            text.Write("^^<"u8);
            text.Write(value.Datatype.Iri.Span);
            text.Write(">"u8);
        }
    }

    /// <summary>Renders an object-property expression: a named property or an inverse modifier.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The rendered bytes.</returns>
    private static Utf8String RenderObjectProperty(OwlObjectPropertyExpression expression)
    {
        ArrayBufferWriter<byte> text = new();
        switch(expression)
        {
            case(OwlObjectPropertyReference reference):
            {
                text.Write("<"u8);
                text.Write(reference.Named.Iri.Span);
                text.Write(">"u8);
                break;
            }
            case(OwlInverseObjectProperty inverse):
            {
                text.Write(OwlManchesterWords.InverseWord);
                text.Write(" <"u8);
                text.Write(inverse.Inverted.Iri.Span);
                text.Write(">"u8);
                break;
            }
            default:
            {
                text.Write(OwlManchesterWords.InverseWord);
                text.Write(" <urn:veritas:invalid>"u8);
                break;
            }
        }

        return ToUtf8String(text);
    }

    //One unit of expression-rendering work: literal bytes, a class expression
    //to expand, or a data range to expand. Parenthesization is decided at push
    //time, so precedence is explicit in the output.
    private readonly record struct Piece(Utf8String? Text, OwlClassExpression? Class, OwlDataRange? Range, bool Parenthesize)
    {
        /// <summary>A literal-text piece.</summary>
        /// <param name="text">The bytes to emit verbatim.</param>
        /// <returns>The piece.</returns>
        public static Piece Of(Utf8String text)
        {
            return new Piece(text, null, null, false);
        }

        /// <summary>A class-expression piece to expand.</summary>
        /// <param name="expression">The class expression.</param>
        /// <param name="parenthesize">Whether a compound expansion parenthesizes.</param>
        /// <returns>The piece.</returns>
        public static Piece Expand(OwlClassExpression expression, bool parenthesize)
        {
            return new Piece(null, expression, null, parenthesize);
        }

        /// <summary>A data-range piece to expand.</summary>
        /// <param name="range">The data range.</param>
        /// <param name="parenthesize">Whether a compound expansion parenthesizes.</param>
        /// <returns>The piece.</returns>
        public static Piece Expand(OwlDataRange range, bool parenthesize)
        {
            return new Piece(null, null, range, parenthesize);
        }
    }

    /// <summary>Renders a class expression through an explicit work stack.</summary>
    /// <param name="expression">The class expression.</param>
    /// <param name="parenthesize">Whether a compound rendering parenthesizes.</param>
    /// <param name="inexpressible">Set when the expression met a shape with no Manchester form.</param>
    /// <returns>The rendered bytes.</returns>
    private static Utf8String RenderClass(OwlClassExpression expression, bool parenthesize, out bool inexpressible)
    {
        ArrayBufferWriter<byte> text = new();
        Stack<Piece> work = new();
        work.Push(Piece.Expand(expression, parenthesize));
        inexpressible = Drain(text, work);

        return ToUtf8String(text);
    }

    /// <summary>Renders a data range through an explicit work stack.</summary>
    /// <param name="range">The data range.</param>
    /// <param name="parenthesize">Whether a compound rendering parenthesizes.</param>
    /// <param name="inexpressible">Set when the range met a shape with no Manchester form.</param>
    /// <returns>The rendered bytes.</returns>
    private static Utf8String RenderDataRange(OwlDataRange range, bool parenthesize, out bool inexpressible)
    {
        ArrayBufferWriter<byte> text = new();
        Stack<Piece> work = new();
        work.Push(Piece.Expand(range, parenthesize));
        inexpressible = Drain(text, work);

        return ToUtf8String(text);
    }

    /// <summary>Drains the work stack into the output, reporting whether any piece had no Manchester form.</summary>
    /// <param name="text">The destination buffer writer.</param>
    /// <param name="work">The work stack.</param>
    /// <returns><see langword="true"/> when a piece met a shape with no Manchester form.</returns>
    private static bool Drain(IBufferWriter<byte> text, Stack<Piece> work)
    {
        bool inexpressible = false;
        while(work.Count > 0)
        {
            Piece piece = work.Pop();
            if(piece.Text is Utf8String literal)
            {
                text.Write(literal.Span);

                continue;
            }

            if(piece.Class is OwlClassExpression expression)
            {
                inexpressible |= ExpandClass(text, work, expression, piece.Parenthesize);

                continue;
            }

            inexpressible |= ExpandDataRange(text, work, piece.Range!, piece.Parenthesize);
        }

        return inexpressible;
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

    /// <summary>Expands one class expression: writes its leaf or pushes its operands, parenthesized when compound.</summary>
    /// <param name="text">The destination buffer writer.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="expression">The class expression.</param>
    /// <param name="parenthesize">Whether a compound expansion parenthesizes.</param>
    /// <returns><see langword="true"/> when the expression has no Manchester form.</returns>
    private static bool ExpandClass(IBufferWriter<byte> text, Stack<Piece> work, OwlClassExpression expression, bool parenthesize)
    {
        bool wrap = parenthesize && !IsAtomic(expression);
        bool inexpressible = false;
        List<Piece> pieces = [];
        if(wrap)
        {
            pieces.Add(Piece.Of(new Utf8String("("u8.ToArray())));
        }

        switch(expression)
        {
            case(OwlClassReference reference):
            {
                pieces.Add(Piece.Of(RenderIri(reference.Class.Iri)));
                break;
            }
            case(OwlObjectOneOf oneOf):
            {
                ArrayBufferWriter<byte> set = new();
                set.Write("{"u8);
                for(int i = 0; i < oneOf.Individuals.Count; i++)
                {
                    if(i > 0)
                    {
                        set.Write(", "u8);
                    }

                    AppendTerm(set, oneOf.Individuals[i]);
                }

                set.Write("}"u8);
                pieces.Add(Piece.Of(ToUtf8String(set)));
                break;
            }
            case(OwlObjectIntersectionOf intersection):
            {
                for(int i = 0; i < intersection.Operands.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(new Utf8String(" and "u8.ToArray())));
                    }

                    pieces.Add(Piece.Expand(intersection.Operands[i], parenthesize: true));
                }

                break;
            }
            case(OwlObjectUnionOf union):
            {
                for(int i = 0; i < union.Operands.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(new Utf8String(" or "u8.ToArray())));
                    }

                    pieces.Add(Piece.Expand(union.Operands[i], parenthesize: true));
                }

                break;
            }
            case(OwlObjectComplementOf complement):
            {
                pieces.Add(Piece.Of(new Utf8String("not "u8.ToArray())));
                pieces.Add(Piece.Expand(complement.Operand, parenthesize: true));
                break;
            }
            case(OwlObjectSomeValuesFrom someValues):
            {
                pieces.Add(Piece.Of(SpaceWord(RenderObjectProperty(someValues.Property), OwlManchesterWords.SomeWord)));
                pieces.Add(Piece.Expand(someValues.Filler, parenthesize: true));
                break;
            }
            case(OwlObjectAllValuesFrom allValues):
            {
                pieces.Add(Piece.Of(SpaceWord(RenderObjectProperty(allValues.Property), OwlManchesterWords.OnlyWord)));
                pieces.Add(Piece.Expand(allValues.Filler, parenthesize: true));
                break;
            }
            case(OwlObjectHasValue hasValue):
            {
                pieces.Add(Piece.Of(RestrictionValue(RenderObjectProperty(hasValue.Property), OwlManchesterWords.ValueWord, RenderTerm(hasValue.Individual))));
                break;
            }
            case(OwlObjectHasSelf hasSelf):
            {
                pieces.Add(Piece.Of(JoinSpaced(RenderObjectProperty(hasSelf.Property), new Utf8String(OwlManchesterWords.SelfWord.ToArray()))));
                break;
            }
            case(OwlObjectCardinality cardinality):
            {
                pieces.Add(Piece.Of(RenderCardinality(RenderObjectProperty(cardinality.Property), cardinality.Kind, cardinality.Cardinality)));
                if(cardinality.Filler is OwlClassExpression filler)
                {
                    pieces.Add(Piece.Of(new Utf8String(" "u8.ToArray())));
                    pieces.Add(Piece.Expand(filler, parenthesize: true));
                }

                break;
            }
            case(OwlDataSomeValuesFrom dataSome) when dataSome.Properties.Count == 1:
            {
                pieces.Add(Piece.Of(SpaceWord(RenderIri(dataSome.Properties[0].Iri), OwlManchesterWords.SomeWord)));
                pieces.Add(Piece.Expand(dataSome.Range, parenthesize: true));
                break;
            }
            case(OwlDataAllValuesFrom dataAll) when dataAll.Properties.Count == 1:
            {
                pieces.Add(Piece.Of(SpaceWord(RenderIri(dataAll.Properties[0].Iri), OwlManchesterWords.OnlyWord)));
                pieces.Add(Piece.Expand(dataAll.Range, parenthesize: true));
                break;
            }
            case(OwlDataHasValue dataHasValue):
            {
                pieces.Add(Piece.Of(RestrictionValue(RenderIri(dataHasValue.Property.Iri), OwlManchesterWords.ValueWord, RenderTerm(dataHasValue.Value))));
                break;
            }
            case(OwlDataCardinality dataCardinality):
            {
                pieces.Add(Piece.Of(RenderCardinality(RenderIri(dataCardinality.Property.Iri), dataCardinality.Kind, dataCardinality.Cardinality)));
                if(dataCardinality.Range is OwlDataRange qualifier)
                {
                    pieces.Add(Piece.Of(new Utf8String(" "u8.ToArray())));
                    pieces.Add(Piece.Expand(qualifier, parenthesize: true));
                }

                break;
            }
            default:
            {
                //An n-ary data quantifier over several properties has no
                //Manchester form; the marker keeps the text readable and the
                //flag drops the whole axiom.
                pieces.Add(Piece.Of(new Utf8String("<urn:veritas:inexpressible>"u8.ToArray())));
                inexpressible = true;
                break;
            }
        }

        if(wrap)
        {
            pieces.Add(Piece.Of(new Utf8String(")"u8.ToArray())));
        }

        PushReversed(work, pieces);

        return inexpressible;
    }

    /// <summary>Expands one data range: writes its leaf or pushes its operands, parenthesized when compound.</summary>
    /// <param name="text">The destination buffer writer.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="range">The data range.</param>
    /// <param name="parenthesize">Whether a compound expansion parenthesizes.</param>
    /// <returns><see langword="true"/> when the range has no Manchester form.</returns>
    private static bool ExpandDataRange(IBufferWriter<byte> text, Stack<Piece> work, OwlDataRange range, bool parenthesize)
    {
        bool wrap = parenthesize && !IsAtomic(range);
        bool inexpressible = false;
        List<Piece> pieces = [];
        if(wrap)
        {
            pieces.Add(Piece.Of(new Utf8String("("u8.ToArray())));
        }

        switch(range)
        {
            case(OwlDatatypeReference reference):
            {
                pieces.Add(Piece.Of(RenderIri(reference.Datatype.Iri)));
                break;
            }
            case(OwlDataOneOf oneOf):
            {
                ArrayBufferWriter<byte> set = new();
                set.Write("{"u8);
                for(int i = 0; i < oneOf.Literals.Count; i++)
                {
                    if(i > 0)
                    {
                        set.Write(", "u8);
                    }

                    AppendLiteral(set, oneOf.Literals[i]);
                }

                set.Write("}"u8);
                pieces.Add(Piece.Of(ToUtf8String(set)));
                break;
            }
            case(OwlDatatypeRestriction restriction):
            {
                ArrayBufferWriter<byte> facets = new();
                facets.Write("<"u8);
                facets.Write(restriction.Datatype.Iri.Span);
                facets.Write(">["u8);
                for(int i = 0; i < restriction.Restrictions.Count; i++)
                {
                    if(i > 0)
                    {
                        facets.Write(", "u8);
                    }

                    facets.Write(FacetWord(restriction.Restrictions[i].Facet));
                    facets.Write(" "u8);
                    AppendLiteral(facets, restriction.Restrictions[i].Value);
                }

                facets.Write("]"u8);
                pieces.Add(Piece.Of(ToUtf8String(facets)));
                break;
            }
            case(OwlDataIntersectionOf intersection):
            {
                for(int i = 0; i < intersection.Ranges.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(new Utf8String(" and "u8.ToArray())));
                    }

                    pieces.Add(Piece.Expand(intersection.Ranges[i], parenthesize: true));
                }

                break;
            }
            case(OwlDataUnionOf union):
            {
                for(int i = 0; i < union.Ranges.Count; i++)
                {
                    if(i > 0)
                    {
                        pieces.Add(Piece.Of(new Utf8String(" or "u8.ToArray())));
                    }

                    pieces.Add(Piece.Expand(union.Ranges[i], parenthesize: true));
                }

                break;
            }
            case(OwlDataComplementOf complement):
            {
                pieces.Add(Piece.Of(new Utf8String("not "u8.ToArray())));
                pieces.Add(Piece.Expand(complement.Range, parenthesize: true));
                break;
            }
            default:
            {
                pieces.Add(Piece.Of(new Utf8String("<urn:veritas:inexpressible>"u8.ToArray())));
                inexpressible = true;
                break;
            }
        }

        if(wrap)
        {
            pieces.Add(Piece.Of(new Utf8String(")"u8.ToArray())));
        }

        PushReversed(work, pieces);

        return inexpressible;
    }

    /// <summary>Renders a property, a space, an operator word, and a trailing space: <c>&lt;p&gt; some&#160;</c>.</summary>
    /// <param name="property">The rendered property.</param>
    /// <param name="word">The operator word bytes.</param>
    /// <returns>The rendered bytes.</returns>
    private static Utf8String SpaceWord(Utf8String property, ReadOnlySpan<byte> word)
    {
        ArrayBufferWriter<byte> text = new();
        text.Write(property.Span);
        text.Write(" "u8);
        text.Write(word);
        text.Write(" "u8);

        return ToUtf8String(text);
    }

    /// <summary>Renders a has-value restriction: <c>&lt;p&gt; value &lt;v&gt;</c>.</summary>
    /// <param name="property">The rendered property.</param>
    /// <param name="word">The restriction word bytes.</param>
    /// <param name="value">The rendered value term.</param>
    /// <returns>The rendered bytes.</returns>
    private static Utf8String RestrictionValue(Utf8String property, ReadOnlySpan<byte> word, Utf8String value)
    {
        ArrayBufferWriter<byte> text = new();
        text.Write(property.Span);
        text.Write(" "u8);
        text.Write(word);
        text.Write(" "u8);
        text.Write(value.Span);

        return ToUtf8String(text);
    }

    /// <summary>Renders a cardinality restriction head: <c>&lt;p&gt; min 1</c>.</summary>
    /// <param name="property">The rendered property.</param>
    /// <param name="kind">The cardinality kind.</param>
    /// <param name="cardinality">The cardinality bound.</param>
    /// <returns>The rendered bytes.</returns>
    private static Utf8String RenderCardinality(Utf8String property, OwlCardinalityKind kind, int cardinality)
    {
        ReadOnlySpan<byte> word = kind switch
        {
            OwlCardinalityKind.Min => OwlManchesterWords.MinWord,
            OwlCardinalityKind.Max => OwlManchesterWords.MaxWord,
            _ => OwlManchesterWords.ExactlyWord
        };

        ArrayBufferWriter<byte> text = new();
        text.Write(property.Span);
        text.Write(" "u8);
        text.Write(word);
        text.Write(" "u8);
        AppendNumber(text, cardinality);

        return ToUtf8String(text);
    }

    /// <summary>Maps a constraining-facet IRI back to its Manchester word or comparison operator.</summary>
    /// <param name="facet">The facet IRI node.</param>
    /// <returns>The facet token bytes.</returns>
    private static ReadOnlySpan<byte> FacetWord(NamedNode facet)
    {
        ReadOnlySpan<byte> iri = facet.Iri.Span;
        foreach(KeyValuePair<Utf8String, Utf8String> named in OwlManchesterWords.NamedFacets)
        {
            if(named.Value.SequenceEqual(iri))
            {
                return named.Key.Span;
            }
        }

        foreach(KeyValuePair<Utf8String, Utf8String> comparison in OwlManchesterWords.ComparisonFacets)
        {
            if(comparison.Value.SequenceEqual(iri))
            {
                return comparison.Key.Span;
            }
        }

        //An unknown facet IRI has no Manchester abbreviation; the raw IRI at
        //least reads back as an unknown facet rather than vanishing.
        return facet.Iri.Span;
    }

    /// <summary>Appends a separator before all but the first member of a list.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="separator">The separator bytes.</param>
    /// <param name="first">The first-member flag, cleared after the first call.</param>
    private static void AppendSeparated(IBufferWriter<byte> output, ReadOnlySpan<byte> separator, ref bool first)
    {
        if(first)
        {
            first = false;

            return;
        }

        output.Write(separator);
    }

    /// <summary>Appends a nonnegative integer as its decimal digits.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="value">The value to write.</param>
    private static void AppendNumber(ArrayBufferWriter<byte> output, int value)
    {
        Span<byte> buffer = output.GetSpan(16);
        Utf8Formatter.TryFormat(value, buffer, out int written);
        output.Advance(written);
    }

    /// <summary>Snapshots a buffer's written bytes as an owned <see cref="Utf8String"/>.</summary>
    /// <param name="buffer">The buffer to snapshot.</param>
    /// <returns>The snapshot.</returns>
    private static Utf8String ToUtf8String(ArrayBufferWriter<byte> buffer)
    {
        return new Utf8String(buffer.WrittenSpan.ToArray());
    }

    /// <summary>Pushes built pieces in reverse so they pop in their built order.</summary>
    /// <param name="work">The work stack.</param>
    /// <param name="pieces">The pieces to push.</param>
    private static void PushReversed(Stack<Piece> work, List<Piece> pieces)
    {
        for(int i = pieces.Count - 1; i >= 0; i--)
        {
            work.Push(pieces[i]);
        }
    }
}
