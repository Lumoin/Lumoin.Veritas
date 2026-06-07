using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>
/// Converts a Manchester-syntax token tree into the structural model:
/// prefix declarations, the ontology header, and frames whose sections
/// become axioms.
/// </summary>
/// <remarks>
/// <para>
/// Conversion runs in two passes: every entity frame first declares its
/// subject, then sections convert in document order — so a restriction's
/// property reads as a data property exactly when a <c>DataProperty:</c>
/// frame (or prior declaration) names it, the typing rule the Manchester
/// note delegates to declarations.
/// </para>
/// <para>
/// Annotated lists parse greedily: after <c>Annotations:</c>, annotations
/// continue across commas while the next segment reads as an annotation
/// (a non-reserved property name followed by a target); the remainder of
/// the segment is the item payload. This resolves the note's grammar
/// ambiguity deterministically and round-trips everything the writer
/// emits. Expression parsing is post-order over an explicit task stack —
/// no call-stack recursion at any nesting depth.
/// </para>
/// </remarks>
internal sealed class OwlManchesterSyntaxConverter
{
    /// <summary>Initialises a converter recording into the supplied bag.</summary>
    /// <param name="diagnostics">The shared diagnostic bag.</param>
    public OwlManchesterSyntaxConverter(DiagnosticBag diagnostics)
    {
        Diagnostics = diagnostics;
        foreach(KeyValuePair<string, string> builtin in OwlManchesterWords.BuiltinPrefixes)
        {
            Prefixes[builtin.Key] = builtin.Value;
        }
    }

    /// <summary>Gets the bag conversion diagnostics record into.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>The source extent of the construct currently converting — the span every conversion diagnostic carries.</summary>
    private SourceSpan CurrentSpan { get; set; }

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

    /// <summary>The prefix table the document's names resolve through; the built-in prefixes are preloaded.</summary>
    private Dictionary<string, string> Prefixes { get; } = [];

    /// <summary>Converted expression values per task, filled in post-order.</summary>
    private Dictionary<ExprTask, object?> Converted { get; } = [];

    /// <summary>The synthetic origin every Manchester-syntax axiom carries — the syntax has no triples to anchor to.</summary>
    private Quad Origin { get; } = new(
        new NamedNode(Utf8String.FromString("urn:veritas:manchester-syntax")),
        new NamedNode(Utf8String.FromString("urn:veritas:manchester-syntax")),
        new NamedNode(Utf8String.FromString("urn:veritas:manchester-syntax")),
        Graph: null);

    //One frame of the document: the keyword token, the subject node for
    //entity frames, and the node range its content occupies.
    private sealed record DocumentFrame(OwlManchesterToken Keyword, OwlManchesterNode? Subject, int Lo, int Hi);

    /// <summary>Converts the whole top-level node list.</summary>
    /// <param name="items">The document's top-level tokens and groups.</param>
    public void ConvertDocument(List<OwlManchesterNode> items)
    {
        List<DocumentFrame> frames = SliceFrames(items);

        //Pass 1: entity frames declare their subjects, so later expressions
        //type their properties by the full census. The pass is silent —
        //resolution faults report once, in pass 2 — and an anonymous
        //individual subject declares nothing.
        foreach(DocumentFrame frame in frames)
        {
            if(OwlManchesterWords.EntityFrames.TryGetValue(frame.Keyword.Text, out OwlEntityKind kind)
                && frame.Subject is { IsAtom: true } subject
                && subject.Atom.Kind != OwlManchesterTokenKind.BlankNode
                && PeekReference(subject) is Utf8String iri)
            {
                Declare(kind, iri);
            }
        }

        //Pass 2: convert in document order.
        foreach(DocumentFrame frame in frames)
        {
            ConvertFrame(items, frame);
        }
    }

    /// <summary>Adds an IRI to the declaration census for its entity kind.</summary>
    /// <param name="kind">The declared entity kind.</param>
    /// <param name="iri">The declared IRI.</param>
    private void Declare(OwlEntityKind kind, Utf8String iri)
    {
        HashSet<Utf8String>? target = kind switch
        {
            OwlEntityKind.Class => DeclaredClasses,
            OwlEntityKind.Datatype => DeclaredDatatypes,
            OwlEntityKind.ObjectProperty => DeclaredObjectProperties,
            OwlEntityKind.DataProperty => DeclaredDataProperties,
            OwlEntityKind.AnnotationProperty => DeclaredAnnotationProperties,
            _ => null
        };

        target?.Add(iri);
    }

    //Slices the top-level items into prefix declarations (registered
    //immediately), the ontology header, and frames. Ontology-level
    //'Annotations:' blocks count as frames only while the header region
    //lasts; once an entity or misc frame begins, 'Annotations:' is a section.
    private List<DocumentFrame> SliceFrames(List<OwlManchesterNode> items)
    {
        List<DocumentFrame> frames = [];
        bool inHeader = true;
        int i = 0;

        while(i < items.Count)
        {
            OwlManchesterNode node = items[i];
            CurrentSpan = node.Span;

            if(node is not { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } token } || !IsFrameStart(token.Text, inHeader))
            {
                Report($"Expected a frame keyword, found '{(node.IsAtom ? node.Atom.Text : "(…)")}'.");
                i++;

                continue;
            }

            if(token.Text == OwlManchesterWords.PrefixKeyword)
            {
                i = RegisterPrefix(items, i);

                continue;
            }

            if(token.Text == OwlManchesterWords.OntologyKeyword)
            {
                i++;
                if(i < items.Count && items[i] is { IsAtom: true, Atom.Kind: OwlManchesterTokenKind.Iri } ontologyIriNode)
                {
                    OntologyIri ??= new NamedNode(Utf8String.FromString(ontologyIriNode.Atom.Text));
                    i++;

                    //An optional version IRI follows; the structural model does not carry it.
                    if(i < items.Count && items[i] is { IsAtom: true, Atom.Kind: OwlManchesterTokenKind.Iri })
                    {
                        i++;
                    }
                }

                continue;
            }

            if(OwlManchesterWords.EntityFrames.ContainsKey(token.Text) || OwlManchesterWords.MiscFrames.Contains(token.Text))
            {
                inHeader = false;
            }

            OwlManchesterNode? subject = null;
            int contentStart = i + 1;
            if(OwlManchesterWords.EntityFrames.ContainsKey(token.Text))
            {
                if(contentStart < items.Count && items[contentStart].IsAtom
                    && items[contentStart].Atom.Kind is OwlManchesterTokenKind.Iri or OwlManchesterTokenKind.Name or OwlManchesterTokenKind.BlankNode)
                {
                    subject = items[contentStart];
                    contentStart++;
                }
                else
                {
                    Report($"The frame '{token.Text}' is missing its subject.");
                }
            }

            //An ontology-annotation block absorbs following 'Annotations:'
            //keywords — they continue the block (or nest within it), they do
            //not start a sibling frame.
            bool annotationBlock = token.Text == OwlManchesterWords.AnnotationsKeyword;
            int end = contentStart;
            while(end < items.Count && !IsFrameBoundary(items[end], inHeader && !annotationBlock))
            {
                end++;
            }

            frames.Add(new DocumentFrame(token, subject, contentStart, end));
            i = end;
        }

        return frames;
    }

    /// <summary>Whether a word starts a new top-level region in the current document phase.</summary>
    /// <param name="word">The raw token text.</param>
    /// <param name="inHeader">Whether the ontology header region is still open.</param>
    /// <returns><see langword="true"/> for frame starts.</returns>
    private static bool IsFrameStart(string word, bool inHeader)
    {
        if(word == OwlManchesterWords.AnnotationsKeyword)
        {
            return inHeader;
        }

        return OwlManchesterWords.IsFrameKeyword(word);
    }

    /// <summary>Whether a node ends the running frame's content range.</summary>
    /// <param name="node">The candidate node.</param>
    /// <param name="inHeader">Whether the ontology header region is still open.</param>
    /// <returns><see langword="true"/> at frame boundaries.</returns>
    private static bool IsFrameBoundary(OwlManchesterNode node, bool inHeader)
    {
        if(node is not { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } token })
        {
            return false;
        }

        if(token.Text == OwlManchesterWords.AnnotationsKeyword)
        {
            //Inside a frame, 'Annotations:' is a section; in the header it
            //opens an ontology-annotation block.
            return inHeader;
        }

        return OwlManchesterWords.IsFrameKeyword(token.Text);
    }

    /// <summary>Registers a <c>Prefix: p: &lt;iri&gt;</c> declaration; returns the index past it.</summary>
    /// <param name="items">The top-level items.</param>
    /// <param name="keywordIndex">The index of the <c>Prefix:</c> token.</param>
    /// <returns>The index of the first unconsumed item.</returns>
    private int RegisterPrefix(List<OwlManchesterNode> items, int keywordIndex)
    {
        int nameIndex = keywordIndex + 1;
        int iriIndex = keywordIndex + 2;

        if(iriIndex < items.Count
            && items[nameIndex] is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } nameToken }
            && items[iriIndex] is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Iri } iriToken })
        {
            Prefixes[nameToken.Text.TrimEnd(':')] = iriToken.Text;

            return keywordIndex + 3;
        }

        Report("A prefix declaration must be 'Prefix: p: <iri>'.");

        return keywordIndex + 1;
    }

    //Dispatches one frame to its converter.
    private void ConvertFrame(List<OwlManchesterNode> items, DocumentFrame frame)
    {
        CurrentSpan = frame.Subject?.Span ?? CurrentSpan;

        string keyword = frame.Keyword.Text;

        if(keyword == OwlManchesterWords.ImportKeyword)
        {
            if(frame.Lo < frame.Hi && items[frame.Lo] is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Iri } iriToken })
            {
                Axioms.Add(new OwlImportAxiom(new NamedNode(Utf8String.FromString(iriToken.Text))) { Origin = Origin });
            }
            else
            {
                Report("'Import:' must be followed by an IRI.");
            }

            return;
        }

        if(keyword == OwlManchesterWords.AnnotationsKeyword)
        {
            //An ontology-level annotation block surfaces as annotation
            //assertions on the ontology IRI; an anonymous ontology has no
            //subject to carry them, so they are consumed. Consecutive
            //'Annotations:' blocks merged into this frame each parse in turn.
            int cursor = frame.Lo;
            bool firstBlock = true;
            while(cursor < frame.Hi)
            {
                //Between blocks the keyword separates siblings; at the very
                //start it nests within the frame's own block and the list
                //parser owns it.
                if(!firstBlock && IsWord(items, cursor, OwlManchesterWords.AnnotationsKeyword))
                {
                    cursor++;
                }

                firstBlock = false;
                int before = cursor;
                ImmutableArray<OwlAnnotation> annotations = ParseAnnotationList(items, ref cursor, frame.Hi);
                if(OntologyIri is NamedNode ontologyIri)
                {
                    foreach(OwlAnnotation annotation in annotations)
                    {
                        Axioms.Add(new OwlAnnotationAssertionAxiom(ontologyIri, annotation.Property, annotation.Value)
                        {
                            Origin = Origin,
                            Annotations = annotation.Annotations
                        });
                    }
                }

                if(cursor == before)
                {
                    //Recovery left the cursor in place (a stray comma); step
                    //over it so the block always drains.
                    cursor++;
                }
            }

            return;
        }

        if(OwlManchesterWords.MiscFrames.Contains(keyword))
        {
            ConvertMiscFrame(items, frame);

            return;
        }

        if(!OwlManchesterWords.EntityFrames.TryGetValue(keyword, out OwlEntityKind kind))
        {
            return;
        }

        if(frame.Subject is not OwlManchesterNode subjectNode)
        {
            return;
        }

        CurrentSpan = subjectNode.Span;

        //A blank-node individual heads a frame without declaring anything.
        RdfTerm subjectTerm;
        Utf8String? subjectIri = null;
        if(subjectNode.Atom.Kind == OwlManchesterTokenKind.BlankNode)
        {
            if(kind != OwlEntityKind.NamedIndividual)
            {
                Report("Only an individual frame may have an anonymous subject.");

                return;
            }

            subjectTerm = new BlankNode(Utf8String.FromString(subjectNode.Atom.Text));
        }
        else
        {
            if(ResolveReference(subjectNode) is not Utf8String resolved)
            {
                return;
            }

            subjectIri = resolved;
            subjectTerm = new NamedNode(resolved);
            Axioms.Add(new OwlDeclarationAxiom(kind, new NamedNode(resolved)) { Origin = Origin });
        }

        foreach((OwlManchesterToken sectionKeyword, SourceSpan keywordSpan, int lo, int hi) in SliceSections(items, frame.Lo, frame.Hi))
        {
            CurrentSpan = keywordSpan;
            ConvertSection(items, kind, subjectTerm, subjectIri, sectionKeyword, lo, hi);
        }
    }

    /// <summary>Slices a frame's content range into sections at section keywords.</summary>
    /// <param name="items">The top-level items.</param>
    /// <param name="lo">The inclusive content start.</param>
    /// <param name="hi">The exclusive content end.</param>
    /// <returns>The sections: the keyword token, its span, and the half-open content range of each.</returns>
    private IEnumerable<(OwlManchesterToken Keyword, SourceSpan KeywordSpan, int Lo, int Hi)> SliceSections(List<OwlManchesterNode> items, int lo, int hi)
    {
        int i = lo;
        while(i < hi)
        {
            if(items[i] is not { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } token } || !OwlManchesterWords.Sections.Contains(token.Text))
            {
                CurrentSpan = items[i].Span;
                Report($"Expected a section keyword, found '{(items[i].IsAtom ? items[i].Atom.Text : "(…)")}'.");
                i++;

                continue;
            }

            int start = i + 1;
            int end = start;
            while(end < hi && !IsSectionBoundary(items, end))
            {
                end++;
            }

            yield return (token, items[i].Span, start, end);

            i = end;
        }
    }

    /// <summary>
    /// Whether the node at a position starts a new section. An
    /// <c>Annotations:</c> keyword is the subtle case: directly after another
    /// section keyword or a comma it opens an annotated-list block inside the
    /// running section; anywhere else it is the annotation section itself.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="position">The candidate position; the caller guarantees a preceding item exists.</param>
    /// <returns><see langword="true"/> at section boundaries.</returns>
    private static bool IsSectionBoundary(List<OwlManchesterNode> items, int position)
    {
        if(items[position] is not { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } token } || !OwlManchesterWords.Sections.Contains(token.Text))
        {
            return false;
        }

        if(token.Text != OwlManchesterWords.AnnotationsKeyword)
        {
            return true;
        }

        OwlManchesterNode previous = items[position - 1];
        if(IsComma(previous))
        {
            return false;
        }

        return previous is not { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } precedingWord }
            || !OwlManchesterWords.Sections.Contains(precedingWord.Text);
    }

    //One annotated item of a section: its annotations and its payload range.
    private sealed record SectionItem(ImmutableArray<OwlAnnotation> Annotations, int Lo, int Hi);

    //Splits a section's content range at top-level commas into annotated
    //items: an item's leading 'Annotations:' block parses greedily, the rest
    //of the segment is the payload.
    private List<SectionItem> SplitAnnotatedItems(List<OwlManchesterNode> items, int lo, int hi)
    {
        List<SectionItem> result = [];
        int cursor = lo;

        while(cursor < hi)
        {
            ImmutableArray<OwlAnnotation> annotations = [];
            if(IsWord(items, cursor, OwlManchesterWords.AnnotationsKeyword))
            {
                cursor++;
                annotations = ParseAnnotationList(items, ref cursor, hi);
            }

            int payloadStart = cursor;
            while(cursor < hi && !IsComma(items[cursor]))
            {
                cursor++;
            }

            result.Add(new SectionItem(annotations, payloadStart, cursor));

            if(cursor < hi)
            {
                cursor++;
            }
        }

        return result;
    }

    //Parses a comma-separated annotation list starting just past an
    //'Annotations:' keyword, nested annotation blocks included, on an
    //explicit frame stack. The list continues across a comma only while the
    //next segment reads as an annotation — a non-reserved property name or
    //IRI followed by a target atom; otherwise the comma belongs to the
    //enclosing item list and parsing stops.
    private ImmutableArray<OwlAnnotation> ParseAnnotationList(List<OwlManchesterNode> items, ref int cursor, int hi)
    {
        //Each stack entry accumulates one list; a nested 'Annotations:'
        //pushes a child whose finished list annotates the parent's next
        //annotation.
        Stack<(ImmutableArray<OwlAnnotation>.Builder List, ImmutableArray<OwlAnnotation> PendingNested)> stack = new();
        stack.Push((ImmutableArray.CreateBuilder<OwlAnnotation>(), []));

        while(true)
        {
            if(IsWord(items, cursor, OwlManchesterWords.AnnotationsKeyword))
            {
                cursor++;
                stack.Push((ImmutableArray.CreateBuilder<OwlAnnotation>(), []));

                continue;
            }

            (ImmutableArray<OwlAnnotation>.Builder list, ImmutableArray<OwlAnnotation> pendingNested) = stack.Pop();

            if(cursor + 1 < hi
                && ResolveReference(items[cursor]) is Utf8String property
                && ToAnnotationValue(items[cursor + 1]) is RdfTerm target)
            {
                list.Add(new OwlAnnotation(new NamedNode(property), target) { Annotations = pendingNested });
                cursor += 2;
            }
            else
            {
                CurrentSpan = cursor < hi ? items[cursor].Span : CurrentSpan;
                Report("Malformed annotation: a property and a target were expected.");

                //Skip to the next comma so the enclosing list can continue.
                while(cursor < hi && !IsComma(items[cursor]))
                {
                    cursor++;
                }
            }

            if(cursor < hi && IsComma(items[cursor]) && PeeksLikeAnnotation(items, cursor + 1, hi))
            {
                cursor++;
                stack.Push((list, []));

                continue;
            }

            //This list is complete: it either becomes the nested annotations
            //of the parent's next annotation, or it is the result.
            if(stack.Count == 0)
            {
                return list.ToImmutable();
            }

            (ImmutableArray<OwlAnnotation>.Builder parent, _) = stack.Pop();
            stack.Push((parent, list.ToImmutable()));
        }
    }

    /// <summary>Whether the nodes at a position read as another annotation: a non-reserved property followed by a target atom.</summary>
    /// <param name="items">The items.</param>
    /// <param name="position">The candidate start position.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <returns><see langword="true"/> when the segment continues the annotation list.</returns>
    private static bool PeeksLikeAnnotation(List<OwlManchesterNode> items, int position, int hi)
    {
        if(position + 1 >= hi)
        {
            return false;
        }

        if(items[position] is not { IsAtom: true } property
            || property.Atom.Kind is not (OwlManchesterTokenKind.Name or OwlManchesterTokenKind.Iri))
        {
            return false;
        }

        if(property.Atom.Kind == OwlManchesterTokenKind.Name
            && (OwlManchesterWords.Operators.Contains(property.Atom.Text) || property.Atom.Text == OwlManchesterWords.AnnotationsKeyword))
        {
            return false;
        }

        OwlManchesterNode target = items[position + 1];

        return target.IsAtom && target.Atom.Kind is OwlManchesterTokenKind.Literal or OwlManchesterTokenKind.Number
            or OwlManchesterTokenKind.Iri or OwlManchesterTokenKind.BlankNode
            || (target.Atom.Kind == OwlManchesterTokenKind.Name && !OwlManchesterWords.Operators.Contains(target.Atom.Text));
    }

    //Section dispatch for entity frames.
    private void ConvertSection(List<OwlManchesterNode> items, OwlEntityKind frameKind, RdfTerm subjectTerm, Utf8String? subjectIri, OwlManchesterToken sectionKeyword, int lo, int hi)
    {
        if(sectionKeyword.Text == OwlManchesterWords.AnnotationsKeyword)
        {
            int cursor = lo;
            ImmutableArray<OwlAnnotation> annotations = ParseAnnotationList(items, ref cursor, hi);
            foreach(OwlAnnotation annotation in annotations)
            {
                Axioms.Add(new OwlAnnotationAssertionAxiom(subjectTerm, annotation.Property, annotation.Value)
                {
                    Origin = Origin,
                    Annotations = annotation.Annotations
                });
            }

            return;
        }

        switch(frameKind)
        {
            case OwlEntityKind.Class when subjectIri is Utf8String classIri:
            {
                ConvertClassSection(items, new OwlClassReference(new NamedNode(classIri)), classIri, sectionKeyword, lo, hi);
                break;
            }

            case OwlEntityKind.ObjectProperty when subjectIri is Utf8String propertyIri:
            {
                ConvertObjectPropertySection(items, new OwlObjectPropertyReference(new NamedNode(propertyIri)), sectionKeyword, lo, hi);
                break;
            }

            case OwlEntityKind.DataProperty when subjectIri is Utf8String propertyIri:
            {
                ConvertDataPropertySection(items, new NamedNode(propertyIri), sectionKeyword, lo, hi);
                break;
            }

            case OwlEntityKind.AnnotationProperty when subjectIri is Utf8String propertyIri:
            {
                ConvertAnnotationPropertySection(items, new NamedNode(propertyIri), sectionKeyword, lo, hi);
                break;
            }

            case OwlEntityKind.Datatype when subjectIri is Utf8String datatypeIri:
            {
                ConvertDatatypeSection(items, new NamedNode(datatypeIri), sectionKeyword, lo, hi);
                break;
            }

            case OwlEntityKind.NamedIndividual:
            {
                ConvertIndividualSection(items, subjectTerm, sectionKeyword, lo, hi);
                break;
            }

            default:
            {
                break;
            }
        }
    }

    private void ConvertClassSection(List<OwlManchesterNode> items, OwlClassReference subject, Utf8String subjectIri, OwlManchesterToken keyword, int lo, int hi)
    {
        switch(keyword.Text)
        {
            case "SubClassOf:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlSubClassOfAxiom(subject, AsClass(ConvertExpression(ExprKind.Description, items, item.Lo, item.Hi))) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "EquivalentTo:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlEquivalentClassesAxiom(subject, AsClass(ConvertExpression(ExprKind.Description, items, item.Lo, item.Hi))) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "DisjointWith:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlDisjointClassesAxiom([subject, AsClass(ConvertExpression(ExprKind.Description, items, item.Lo, item.Hi))]) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "DisjointUnionOf:":
            {
                (ImmutableArray<OwlAnnotation> annotations, int start) = LeadingAnnotations(items, lo, hi);
                List<OwlClassExpression> operands = [];
                foreach((int itemLo, int itemHi) in SplitPlainItems(items, start, hi))
                {
                    operands.Add(AsClass(ConvertExpression(ExprKind.Description, items, itemLo, itemHi)));
                }

                Axioms.Add(new OwlDisjointUnionAxiom(new NamedNode(subjectIri), operands) { Origin = Origin, Annotations = annotations });
                break;
            }

            case "HasKey:":
            {
                (ImmutableArray<OwlAnnotation> annotations, int start) = LeadingAnnotations(items, lo, hi);
                List<OwlObjectPropertyExpression> objectKeys = [];
                List<NamedNode> dataKeys = [];
                for(int i = start; i < hi; i++)
                {
                    CurrentSpan = items[i].Span;

                    if(IsWord(items, i, "inverse"))
                    {
                        if(i + 1 < hi && ResolveReference(items[i + 1]) is Utf8String inverted)
                        {
                            objectKeys.Add(new OwlInverseObjectProperty(new NamedNode(inverted)));
                            i++;
                        }
                        else
                        {
                            Report("'inverse' must be followed by an object property.");
                        }

                        continue;
                    }

                    if(ResolveReference(items[i]) is not Utf8String key)
                    {
                        continue;
                    }

                    if(DeclaredDataProperties.Contains(key))
                    {
                        dataKeys.Add(new NamedNode(key));
                    }
                    else
                    {
                        objectKeys.Add(new OwlObjectPropertyReference(new NamedNode(key)));
                    }
                }

                Axioms.Add(new OwlHasKeyAxiom(subject, objectKeys, dataKeys) { Origin = Origin, Annotations = annotations });
                break;
            }

            default:
            {
                ReportUnexpectedSection(keyword);
                break;
            }
        }
    }

    private void ConvertObjectPropertySection(List<OwlManchesterNode> items, OwlObjectPropertyReference subject, OwlManchesterToken keyword, int lo, int hi)
    {
        switch(keyword.Text)
        {
            case "Domain:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlObjectPropertyDomainAxiom(subject, AsClass(ConvertExpression(ExprKind.Description, items, item.Lo, item.Hi))) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "Range:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlObjectPropertyRangeAxiom(subject, AsClass(ConvertExpression(ExprKind.Description, items, item.Lo, item.Hi))) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "SubPropertyOf:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlSubObjectPropertyOfAxiom(subject, ParseObjectPropertyExpression(items, item.Lo, item.Hi)) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "EquivalentTo:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlEquivalentObjectPropertiesAxiom(subject, ParseObjectPropertyExpression(items, item.Lo, item.Hi)) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "DisjointWith:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlDisjointObjectPropertiesAxiom([subject, ParseObjectPropertyExpression(items, item.Lo, item.Hi)]) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "InverseOf:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlInverseObjectPropertiesAxiom(subject, ParseObjectPropertyExpression(items, item.Lo, item.Hi)) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "Characteristics:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(item.Lo < item.Hi
                        && items[item.Lo] is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } word }
                        && OwlManchesterWords.Characteristics.TryGetValue(word.Text, out OwlPropertyCharacteristic characteristic))
                    {
                        Axioms.Add(new OwlObjectPropertyCharacteristicAxiom(characteristic, subject) { Origin = Origin, Annotations = item.Annotations });
                    }
                    else
                    {
                        CurrentSpan = item.Lo < item.Hi ? items[item.Lo].Span : CurrentSpan;
                        Report("Unknown object property characteristic.");
                    }
                }

                break;
            }

            case "SubPropertyChain:":
            {
                (ImmutableArray<OwlAnnotation> annotations, int start) = LeadingAnnotations(items, lo, hi);
                List<OwlObjectPropertyExpression> chain = [];
                int linkStart = start;
                for(int i = start; i <= hi; i++)
                {
                    if(i == hi || IsWord(items, i, "o"))
                    {
                        if(linkStart < i)
                        {
                            chain.Add(ParseObjectPropertyExpression(items, linkStart, i));
                        }

                        linkStart = i + 1;
                    }
                }

                if(chain.Count >= 2)
                {
                    Axioms.Add(new OwlPropertyChainAxiom(chain, subject) { Origin = Origin, Annotations = annotations });
                }
                else
                {
                    Report("A sub-property chain needs at least two links.");
                }

                break;
            }

            default:
            {
                ReportUnexpectedSection(keyword);
                break;
            }
        }
    }

    private void ConvertDataPropertySection(List<OwlManchesterNode> items, NamedNode subject, OwlManchesterToken keyword, int lo, int hi)
    {
        switch(keyword.Text)
        {
            case "Domain:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlDataPropertyDomainAxiom(subject, AsClass(ConvertExpression(ExprKind.Description, items, item.Lo, item.Hi))) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "Range:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlDataPropertyRangeAxiom(subject, AsDataRange(ConvertExpression(ExprKind.DataRange, items, item.Lo, item.Hi))) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "SubPropertyOf:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleReference(items, item.Lo, item.Hi) is Utf8String super)
                    {
                        Axioms.Add(new OwlSubDataPropertyOfAxiom(subject, new NamedNode(super)) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            case "EquivalentTo:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleReference(items, item.Lo, item.Hi) is Utf8String other)
                    {
                        Axioms.Add(new OwlEquivalentDataPropertiesAxiom(subject, new NamedNode(other)) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            case "DisjointWith:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleReference(items, item.Lo, item.Hi) is Utf8String other)
                    {
                        Axioms.Add(new OwlDisjointDataPropertiesAxiom([subject, new NamedNode(other)]) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            case "Characteristics:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(IsWord(items, item.Lo, "Functional"))
                    {
                        Axioms.Add(new OwlFunctionalDataPropertyAxiom(subject) { Origin = Origin, Annotations = item.Annotations });
                    }
                    else
                    {
                        CurrentSpan = item.Lo < item.Hi ? items[item.Lo].Span : CurrentSpan;
                        Report("A data property's only characteristic is 'Functional'.");
                    }
                }

                break;
            }

            default:
            {
                ReportUnexpectedSection(keyword);
                break;
            }
        }
    }

    private void ConvertAnnotationPropertySection(List<OwlManchesterNode> items, NamedNode subject, OwlManchesterToken keyword, int lo, int hi)
    {
        switch(keyword.Text)
        {
            case "Domain:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleReference(items, item.Lo, item.Hi) is Utf8String domain)
                    {
                        Axioms.Add(new OwlAnnotationPropertyDomainAxiom(subject, new NamedNode(domain)) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            case "Range:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleReference(items, item.Lo, item.Hi) is Utf8String range)
                    {
                        Axioms.Add(new OwlAnnotationPropertyRangeAxiom(subject, new NamedNode(range)) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            case "SubPropertyOf:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleReference(items, item.Lo, item.Hi) is Utf8String super)
                    {
                        Axioms.Add(new OwlSubAnnotationPropertyOfAxiom(subject, new NamedNode(super)) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            default:
            {
                ReportUnexpectedSection(keyword);
                break;
            }
        }
    }

    private void ConvertDatatypeSection(List<OwlManchesterNode> items, NamedNode subject, OwlManchesterToken keyword, int lo, int hi)
    {
        if(keyword.Text == "EquivalentTo:")
        {
            (ImmutableArray<OwlAnnotation> annotations, int start) = LeadingAnnotations(items, lo, hi);
            Axioms.Add(new OwlDatatypeDefinitionAxiom(subject, AsDataRange(ConvertExpression(ExprKind.DataRange, items, start, hi))) { Origin = Origin, Annotations = annotations });

            return;
        }

        ReportUnexpectedSection(keyword);
    }

    private void ConvertIndividualSection(List<OwlManchesterNode> items, RdfTerm subject, OwlManchesterToken keyword, int lo, int hi)
    {
        switch(keyword.Text)
        {
            case "Types:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    Axioms.Add(new OwlClassAssertionAxiom(AsClass(ConvertExpression(ExprKind.Description, items, item.Lo, item.Hi)), subject) { Origin = Origin, Annotations = item.Annotations });
                }

                break;
            }

            case "Facts:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    ConvertFact(items, subject, item);
                }

                break;
            }

            case "SameAs:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleIndividual(items, item.Lo, item.Hi) is RdfTerm other)
                    {
                        Axioms.Add(new OwlSameIndividualAxiom(subject, other) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            case "DifferentFrom:":
            {
                foreach(SectionItem item in SplitAnnotatedItems(items, lo, hi))
                {
                    if(SingleIndividual(items, item.Lo, item.Hi) is RdfTerm other)
                    {
                        Axioms.Add(new OwlDifferentIndividualsAxiom([subject, other]) { Origin = Origin, Annotations = item.Annotations });
                    }
                }

                break;
            }

            default:
            {
                ReportUnexpectedSection(keyword);
                break;
            }
        }
    }

    //One fact: '[not] property target', a data assertion when the target is
    //a literal or the property is a declared data property.
    private void ConvertFact(List<OwlManchesterNode> items, RdfTerm subject, SectionItem item)
    {
        int cursor = item.Lo;
        bool negative = IsWord(items, cursor, "not");
        if(negative)
        {
            cursor++;
        }

        if(cursor + 1 >= item.Hi || ResolveReference(items[cursor]) is not Utf8String property)
        {
            CurrentSpan = cursor < item.Hi ? items[cursor].Span : CurrentSpan;
            Report("A fact must be '[not] property target'.");

            return;
        }

        OwlManchesterNode targetNode = items[cursor + 1];
        CurrentSpan = targetNode.Span;

        bool dataFact = DeclaredDataProperties.Contains(property)
            || (targetNode.IsAtom && targetNode.Atom.Kind is OwlManchesterTokenKind.Literal or OwlManchesterTokenKind.Number);

        if(dataFact)
        {
            if(ToLiteral(targetNode) is not Literal literal)
            {
                Report("A data fact's target must be a literal.");

                return;
            }

            Axioms.Add(negative
                ? new OwlNegativeDataPropertyAssertionAxiom(subject, new NamedNode(property), literal) { Origin = Origin, Annotations = item.Annotations }
                : new OwlDataPropertyAssertionAxiom(subject, new NamedNode(property), literal) { Origin = Origin, Annotations = item.Annotations });

            return;
        }

        if(ToIndividual(targetNode) is not RdfTerm individual)
        {
            return;
        }

        Axioms.Add(negative
            ? new OwlNegativeObjectPropertyAssertionAxiom(subject, new OwlObjectPropertyReference(new NamedNode(property)), individual) { Origin = Origin, Annotations = item.Annotations }
            : new OwlObjectPropertyAssertionAxiom(subject, new NamedNode(property), individual) { Origin = Origin, Annotations = item.Annotations });
    }

    //Misc n-ary frames: optional leading annotations, then a comma list.
    private void ConvertMiscFrame(List<OwlManchesterNode> items, DocumentFrame frame)
    {
        (ImmutableArray<OwlAnnotation> annotations, int start) = LeadingAnnotations(items, frame.Lo, frame.Hi);
        List<(int Lo, int Hi)> parts = SplitPlainItems(items, start, frame.Hi);

        switch(frame.Keyword.Text)
        {
            case "EquivalentClasses:":
            {
                List<OwlClassExpression> expressions = [];
                foreach((int lo, int hi) in parts)
                {
                    expressions.Add(AsClass(ConvertExpression(ExprKind.Description, items, lo, hi)));
                }

                ForEachPair(expressions.Count, (first, second) =>
                    Axioms.Add(new OwlEquivalentClassesAxiom(expressions[first], expressions[second]) { Origin = Origin, Annotations = annotations }));
                break;
            }

            case "DisjointClasses:":
            {
                List<OwlClassExpression> expressions = [];
                foreach((int lo, int hi) in parts)
                {
                    expressions.Add(AsClass(ConvertExpression(ExprKind.Description, items, lo, hi)));
                }

                Axioms.Add(new OwlDisjointClassesAxiom(expressions) { Origin = Origin, Annotations = annotations });
                break;
            }

            case "EquivalentProperties:":
            case "DisjointProperties:":
            {
                List<Utf8String> properties = [];
                foreach((int lo, int hi) in parts)
                {
                    if(SingleReference(items, lo, hi) is Utf8String property)
                    {
                        properties.Add(property);
                    }
                }

                bool data = properties.Count > 0 && DeclaredDataProperties.Contains(properties[0]);
                if(frame.Keyword.Text == "EquivalentProperties:")
                {
                    ForEachPair(properties.Count, (first, second) => Axioms.Add(data
                        ? new OwlEquivalentDataPropertiesAxiom(new NamedNode(properties[first]), new NamedNode(properties[second])) { Origin = Origin, Annotations = annotations }
                        : new OwlEquivalentObjectPropertiesAxiom(new OwlObjectPropertyReference(new NamedNode(properties[first])), new OwlObjectPropertyReference(new NamedNode(properties[second]))) { Origin = Origin, Annotations = annotations }));
                }
                else if(data)
                {
                    List<NamedNode> nodes = [];
                    foreach(Utf8String property in properties)
                    {
                        nodes.Add(new NamedNode(property));
                    }

                    Axioms.Add(new OwlDisjointDataPropertiesAxiom(nodes) { Origin = Origin, Annotations = annotations });
                }
                else
                {
                    List<OwlObjectPropertyExpression> nodes = [];
                    foreach(Utf8String property in properties)
                    {
                        nodes.Add(new OwlObjectPropertyReference(new NamedNode(property)));
                    }

                    Axioms.Add(new OwlDisjointObjectPropertiesAxiom(nodes) { Origin = Origin, Annotations = annotations });
                }

                break;
            }

            case "SameIndividual:":
            {
                List<RdfTerm> individuals = CollectIndividuals(items, parts);
                ForEachPair(individuals.Count, (first, second) =>
                    Axioms.Add(new OwlSameIndividualAxiom(individuals[first], individuals[second]) { Origin = Origin, Annotations = annotations }));
                break;
            }

            case "DifferentIndividuals:":
            {
                List<RdfTerm> individuals = CollectIndividuals(items, parts);
                Axioms.Add(new OwlDifferentIndividualsAxiom(individuals) { Origin = Origin, Annotations = annotations });
                break;
            }

            default:
            {
                break;
            }
        }
    }

    private List<RdfTerm> CollectIndividuals(List<OwlManchesterNode> items, List<(int Lo, int Hi)> parts)
    {
        List<RdfTerm> individuals = [];
        foreach((int lo, int hi) in parts)
        {
            if(SingleIndividual(items, lo, hi) is RdfTerm individual)
            {
                individuals.Add(individual);
            }
        }

        return individuals;
    }

    private static void ForEachPair(int count, System.Action<int, int> emit)
    {
        for(int i = 0; i < count; i++)
        {
            for(int j = i + 1; j < count; j++)
            {
                emit(i, j);
            }
        }
    }

    /// <summary>Parses an optional leading <c>Annotations:</c> block; returns the annotations and the index past them.</summary>
    /// <param name="items">The items.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <returns>The annotations and the first content index.</returns>
    private (ImmutableArray<OwlAnnotation> Annotations, int Start) LeadingAnnotations(List<OwlManchesterNode> items, int lo, int hi)
    {
        if(!IsWord(items, lo, OwlManchesterWords.AnnotationsKeyword))
        {
            return ([], lo);
        }

        int cursor = lo + 1;
        ImmutableArray<OwlAnnotation> annotations = ParseAnnotationList(items, ref cursor, hi);

        //The list's trailing comma, when the next segment is payload rather
        //than another annotation, separates the block from the content.
        if(cursor < hi && IsComma(items[cursor]))
        {
            cursor++;
        }

        return (annotations, cursor);
    }

    /// <summary>Splits a range at top-level commas into plain payload ranges.</summary>
    /// <param name="items">The items.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <returns>The half-open item ranges.</returns>
    private static List<(int Lo, int Hi)> SplitPlainItems(List<OwlManchesterNode> items, int lo, int hi)
    {
        List<(int, int)> parts = [];
        int start = lo;
        for(int i = lo; i <= hi; i++)
        {
            if(i == hi || IsComma(items[i]))
            {
                parts.Add((start, i));
                start = i + 1;
            }
        }

        return parts;
    }

    //Object property expressions: an IRI, or 'inverse' followed by an IRI.
    private OwlObjectPropertyExpression ParseObjectPropertyExpression(List<OwlManchesterNode> items, int lo, int hi)
    {
        if(lo < hi && IsWord(items, lo, "inverse"))
        {
            if(lo + 1 < hi && ResolveReference(items[lo + 1]) is Utf8String inverted)
            {
                return new OwlInverseObjectProperty(new NamedNode(inverted));
            }

            CurrentSpan = items[lo].Span;
            Report("'inverse' must be followed by an object property.");

            return InvalidObjectProperty();
        }

        if(SingleReference(items, lo, hi) is Utf8String property)
        {
            return new OwlObjectPropertyReference(new NamedNode(property));
        }

        return InvalidObjectProperty();
    }

    private static OwlObjectPropertyReference InvalidObjectProperty()
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")));
    }

    //The expression engine: post-order evaluation of range tasks over an
    //explicit stack, the same dependency discipline the functional-syntax
    //converter uses for its constructor tree.
    private enum ExprKind
    {
        /// <summary>A class description: a disjunction of conjunctions.</summary>
        Description,

        /// <summary>A conjunction of primaries, <c>that</c> included as a separator.</summary>
        Conjunction,

        /// <summary>A primary: optional negation around an atomic or a restriction.</summary>
        Primary,

        /// <summary>A data range: a disjunction of data conjunctions.</summary>
        DataRange,

        /// <summary>A conjunction of data primaries.</summary>
        DataConjunction,

        /// <summary>A data primary: optional negation around a datatype, an enumeration, a facet restriction, or a group.</summary>
        DataPrimary
    }

    //One expression task: a kind over a node-list range. List identity plus
    //the range identifies the task.
    private readonly record struct ExprTask(ExprKind Kind, List<OwlManchesterNode> Items, int Lo, int Hi);

    private object? ConvertExpression(ExprKind kind, List<OwlManchesterNode> items, int lo, int hi)
    {
        ExprTask root = new(kind, items, lo, hi);
        Stack<ExprTask> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            ExprTask task = work.Peek();
            if(Converted.ContainsKey(task))
            {
                work.Pop();

                continue;
            }

            bool ready = true;
            foreach(ExprTask dependency in Dependencies(task))
            {
                if(!Converted.ContainsKey(dependency))
                {
                    ready = false;
                    work.Push(dependency);
                }
            }

            if(!ready)
            {
                continue;
            }

            Converted[task] = Construct(task);
            work.Pop();
        }

        object? result = Converted[root];
        Converted.Clear();

        return result;
    }

    //The subtasks a task's construction reads, recomputed deterministically.
    private List<ExprTask> Dependencies(ExprTask task)
    {
        List<ExprTask> dependencies = [];

        switch(task.Kind)
        {
            case ExprKind.Description:
            {
                foreach((int lo, int hi) in SplitAtWord(task.Items, task.Lo, task.Hi, "or", null))
                {
                    dependencies.Add(new ExprTask(ExprKind.Conjunction, task.Items, lo, hi));
                }

                break;
            }

            case ExprKind.Conjunction:
            {
                foreach((int lo, int hi) in SplitAtWord(task.Items, task.Lo, task.Hi, "and", "that"))
                {
                    dependencies.Add(new ExprTask(ExprKind.Primary, task.Items, lo, hi));
                }

                break;
            }

            case ExprKind.DataRange:
            {
                foreach((int lo, int hi) in SplitAtWord(task.Items, task.Lo, task.Hi, "or", null))
                {
                    dependencies.Add(new ExprTask(ExprKind.DataConjunction, task.Items, lo, hi));
                }

                break;
            }

            case ExprKind.DataConjunction:
            {
                foreach((int lo, int hi) in SplitAtWord(task.Items, task.Lo, task.Hi, "and", null))
                {
                    dependencies.Add(new ExprTask(ExprKind.DataPrimary, task.Items, lo, hi));
                }

                break;
            }

            case ExprKind.Primary:
            {
                PrimaryDependencies(task, dependencies);
                break;
            }

            case ExprKind.DataPrimary:
            {
                int lo = SkipNots(task.Items, task.Lo, task.Hi);
                if(task.Hi - lo == 1 && task.Items[lo] is { IsAtom: false, GroupKind: OwlManchesterGroupKind.Paren } group)
                {
                    dependencies.Add(new ExprTask(ExprKind.DataRange, group.Children, 0, group.Children.Count));
                }

                break;
            }

            default:
            {
                break;
            }
        }

        return dependencies;
    }

    private void PrimaryDependencies(ExprTask task, List<ExprTask> dependencies)
    {
        int lo = SkipNots(task.Items, task.Lo, task.Hi);
        int hi = task.Hi;

        if(hi - lo == 1 && task.Items[lo] is { IsAtom: false, GroupKind: OwlManchesterGroupKind.Paren } group)
        {
            dependencies.Add(new ExprTask(ExprKind.Description, group.Children, 0, group.Children.Count));

            return;
        }

        if(hi - lo < 2)
        {
            return;
        }

        //A restriction: the filler after the operator is the dependency.
        (int operatorIndex, bool data) = LocateRestriction(task.Items, lo, hi);
        if(operatorIndex < 0)
        {
            return;
        }

        string op = task.Items[operatorIndex].Atom.Text;
        if(op is "some" or "only")
        {
            dependencies.Add(new ExprTask(data ? ExprKind.DataPrimary : ExprKind.Primary, task.Items, operatorIndex + 1, hi));
        }
        else if(op is "min" or "max" or "exactly" && operatorIndex + 2 < hi)
        {
            dependencies.Add(new ExprTask(data ? ExprKind.DataPrimary : ExprKind.Primary, task.Items, operatorIndex + 2, hi));
        }
    }

    /// <summary>Finds a restriction's operator word and decides the data branch by the property's declaration.</summary>
    /// <param name="items">The items.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <returns>The operator index, or −1, and whether the restriction is a data restriction.</returns>
    private (int OperatorIndex, bool Data) LocateRestriction(List<OwlManchesterNode> items, int lo, int hi)
    {
        int propertyIndex = lo;
        bool inverse = IsWord(items, lo, "inverse");
        if(inverse)
        {
            propertyIndex++;
        }

        int operatorIndex = propertyIndex + 1;
        if(operatorIndex >= hi
            || items[operatorIndex] is not { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } op }
            || op.Text is not ("some" or "only" or "value" or "Self" or "min" or "max" or "exactly"))
        {
            return (-1, false);
        }

        bool data = !inverse
            && items[propertyIndex].IsAtom
            && PeekReference(items[propertyIndex]) is Utf8String property
            && DeclaredDataProperties.Contains(property);

        return (operatorIndex, data);
    }

    //Constructs a task's value from its already-converted dependencies.
    private object? Construct(ExprTask task)
    {
        switch(task.Kind)
        {
            case ExprKind.Description:
            case ExprKind.DataRange:
            {
                string separator = "or";
                ExprKind part = task.Kind == ExprKind.Description ? ExprKind.Conjunction : ExprKind.DataConjunction;
                List<(int, int)> ranges = SplitAtWord(task.Items, task.Lo, task.Hi, separator, null);
                if(ranges.Count == 1)
                {
                    return Converted[new ExprTask(part, task.Items, ranges[0].Item1, ranges[0].Item2)];
                }

                if(task.Kind == ExprKind.Description)
                {
                    List<OwlClassExpression> operands = [];
                    foreach((int lo, int hi) in ranges)
                    {
                        operands.Add(AsClass(Converted[new ExprTask(part, task.Items, lo, hi)]));
                    }

                    return new OwlObjectUnionOf(operands);
                }

                List<OwlDataRange> dataOperands = [];
                foreach((int lo, int hi) in ranges)
                {
                    dataOperands.Add(AsDataRange(Converted[new ExprTask(part, task.Items, lo, hi)]));
                }

                return new OwlDataUnionOf(dataOperands);
            }

            case ExprKind.Conjunction:
            case ExprKind.DataConjunction:
            {
                ExprKind part = task.Kind == ExprKind.Conjunction ? ExprKind.Primary : ExprKind.DataPrimary;
                List<(int, int)> ranges = SplitAtWord(task.Items, task.Lo, task.Hi, "and", task.Kind == ExprKind.Conjunction ? "that" : null);
                if(ranges.Count == 1)
                {
                    return Converted[new ExprTask(part, task.Items, ranges[0].Item1, ranges[0].Item2)];
                }

                if(task.Kind == ExprKind.Conjunction)
                {
                    List<OwlClassExpression> operands = [];
                    foreach((int lo, int hi) in ranges)
                    {
                        operands.Add(AsClass(Converted[new ExprTask(part, task.Items, lo, hi)]));
                    }

                    return new OwlObjectIntersectionOf(operands);
                }

                List<OwlDataRange> dataOperands = [];
                foreach((int lo, int hi) in ranges)
                {
                    dataOperands.Add(AsDataRange(Converted[new ExprTask(part, task.Items, lo, hi)]));
                }

                return new OwlDataIntersectionOf(dataOperands);
            }

            case ExprKind.Primary:
            {
                return ConstructPrimary(task);
            }

            case ExprKind.DataPrimary:
            {
                return ConstructDataPrimary(task);
            }

            default:
            {
                return null;
            }
        }
    }

    private object? ConstructPrimary(ExprTask task)
    {
        int lo = SkipNots(task.Items, task.Lo, task.Hi);
        int nots = lo - task.Lo;
        object? core = ConstructPrimaryCore(task, lo);

        for(int i = 0; i < nots; i++)
        {
            core = new OwlObjectComplementOf(AsClass(core));
        }

        return core;
    }

    private object? ConstructPrimaryCore(ExprTask task, int lo)
    {
        List<OwlManchesterNode> items = task.Items;
        int hi = task.Hi;

        if(lo >= hi)
        {
            Report("A class expression was expected.");

            return null;
        }

        CurrentSpan = items[lo].Span;

        if(hi - lo == 1)
        {
            OwlManchesterNode node = items[lo];
            if(!node.IsAtom)
            {
                if(node.GroupKind == OwlManchesterGroupKind.Paren)
                {
                    return Converted[new ExprTask(ExprKind.Description, node.Children, 0, node.Children.Count)];
                }

                if(node.GroupKind == OwlManchesterGroupKind.Brace)
                {
                    List<RdfTerm> individuals = [];
                    foreach((int itemLo, int itemHi) in SplitPlainItems(node.Children, 0, node.Children.Count))
                    {
                        if(SingleIndividual(node.Children, itemLo, itemHi) is RdfTerm individual)
                        {
                            individuals.Add(individual);
                        }
                    }

                    return new OwlObjectOneOf(individuals);
                }

                Report("A facet list cannot stand alone as a class expression.");

                return null;
            }

            if(ResolveReference(node) is Utf8String classIri)
            {
                return new OwlClassReference(new NamedNode(classIri));
            }

            return null;
        }

        //A restriction.
        (int operatorIndex, bool data) = LocateRestriction(items, lo, hi);
        if(operatorIndex < 0)
        {
            Report("Malformed class expression.");

            return null;
        }

        bool inverse = IsWord(items, lo, "inverse");
        int propertyIndex = inverse ? lo + 1 : lo;
        if(ResolveReference(items[propertyIndex]) is not Utf8String propertyIri)
        {
            return null;
        }

        OwlObjectPropertyExpression objectProperty = inverse
            ? new OwlInverseObjectProperty(new NamedNode(propertyIri))
            : new OwlObjectPropertyReference(new NamedNode(propertyIri));
        NamedNode dataProperty = new(propertyIri);
        string op = items[operatorIndex].Atom.Text;

        switch(op)
        {
            case "some" or "only":
            {
                if(data)
                {
                    OwlDataRange range = AsDataRange(Converted[new ExprTask(ExprKind.DataPrimary, items, operatorIndex + 1, hi)]);

                    return op == "some"
                        ? new OwlDataSomeValuesFrom([dataProperty], range)
                        : new OwlDataAllValuesFrom([dataProperty], range);
                }

                OwlClassExpression filler = AsClass(Converted[new ExprTask(ExprKind.Primary, items, operatorIndex + 1, hi)]);

                return op == "some"
                    ? new OwlObjectSomeValuesFrom(objectProperty, filler)
                    : new OwlObjectAllValuesFrom(objectProperty, filler);
            }

            case "value":
            {
                if(operatorIndex + 1 >= hi)
                {
                    Report("'value' must be followed by a target.");

                    return null;
                }

                OwlManchesterNode target = items[operatorIndex + 1];
                if(data || (target.IsAtom && target.Atom.Kind is OwlManchesterTokenKind.Literal or OwlManchesterTokenKind.Number))
                {
                    return ToLiteral(target) is Literal literal ? new OwlDataHasValue(dataProperty, literal) : null;
                }

                return ToIndividual(target) is RdfTerm individual ? new OwlObjectHasValue(objectProperty, individual) : null;
            }

            case "Self":
            {
                return new OwlObjectHasSelf(objectProperty);
            }

            case "min" or "max" or "exactly":
            {
                if(operatorIndex + 1 >= hi
                    || items[operatorIndex + 1] is not { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Number } numberToken }
                    || !int.TryParse(numberToken.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int bound))
                {
                    Report($"'{op}' must be followed by a nonnegative integer.");

                    return null;
                }

                OwlCardinalityKind kind = op switch
                {
                    "min" => OwlCardinalityKind.Min,
                    "max" => OwlCardinalityKind.Max,
                    _ => OwlCardinalityKind.Exact
                };

                bool qualified = operatorIndex + 2 < hi;
                if(data)
                {
                    OwlDataRange? range = qualified ? AsDataRange(Converted[new ExprTask(ExprKind.DataPrimary, items, operatorIndex + 2, hi)]) : null;

                    return new OwlDataCardinality(kind, bound, dataProperty, range);
                }

                OwlClassExpression? cardinalityFiller = qualified ? AsClass(Converted[new ExprTask(ExprKind.Primary, items, operatorIndex + 2, hi)]) : null;

                return new OwlObjectCardinality(kind, bound, objectProperty, cardinalityFiller);
            }

            default:
            {
                return null;
            }
        }
    }

    private object? ConstructDataPrimary(ExprTask task)
    {
        int lo = SkipNots(task.Items, task.Lo, task.Hi);
        int nots = lo - task.Lo;
        object? core = ConstructDataPrimaryCore(task, lo);

        for(int i = 0; i < nots; i++)
        {
            core = new OwlDataComplementOf(AsDataRange(core));
        }

        return core;
    }

    private object? ConstructDataPrimaryCore(ExprTask task, int lo)
    {
        List<OwlManchesterNode> items = task.Items;
        int hi = task.Hi;

        if(lo >= hi)
        {
            Report("A data range was expected.");

            return null;
        }

        CurrentSpan = items[lo].Span;

        if(hi - lo == 1)
        {
            OwlManchesterNode node = items[lo];
            if(!node.IsAtom)
            {
                if(node.GroupKind == OwlManchesterGroupKind.Paren)
                {
                    return Converted[new ExprTask(ExprKind.DataRange, node.Children, 0, node.Children.Count)];
                }

                if(node.GroupKind == OwlManchesterGroupKind.Brace)
                {
                    List<Literal> literals = [];
                    foreach((int itemLo, int itemHi) in SplitPlainItems(node.Children, 0, node.Children.Count))
                    {
                        if(itemLo < itemHi && ToLiteral(node.Children[itemLo]) is Literal literal)
                        {
                            literals.Add(literal);
                        }
                    }

                    return new OwlDataOneOf(literals);
                }

                Report("A facet list must follow a datatype.");

                return null;
            }

            return ResolveDatatype(node) is Utf8String datatype ? new OwlDatatypeReference(new NamedNode(datatype)) : null;
        }

        if(hi - lo == 2 && items[lo + 1] is { IsAtom: false, GroupKind: OwlManchesterGroupKind.Bracket } facets)
        {
            if(ResolveDatatype(items[lo]) is not Utf8String datatype)
            {
                return null;
            }

            return new OwlDatatypeRestriction(new NamedNode(datatype), ParseFacets(facets.Children));
        }

        Report("Malformed data range.");

        return null;
    }

    //Facet lists: '(facet literal)' pairs, comma-separated, where the facet
    //is a named word or a comparison operator.
    private List<OwlFacetRestriction> ParseFacets(List<OwlManchesterNode> nodes)
    {
        List<OwlFacetRestriction> facets = [];
        foreach((int lo, int hi) in SplitPlainItems(nodes, 0, nodes.Count))
        {
            if(hi - lo != 2)
            {
                CurrentSpan = lo < nodes.Count ? nodes[lo].Span : CurrentSpan;
                Report("A facet restriction must be 'facet value'.");

                continue;
            }

            OwlManchesterNode facetNode = nodes[lo];
            CurrentSpan = facetNode.Span;

            string? facetIri = facetNode switch
            {
                { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } word } when OwlManchesterWords.NamedFacets.TryGetValue(word.Text, out string? named) => named,
                { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Comparison } op } when OwlManchesterWords.ComparisonFacets.TryGetValue(op.Text, out string? comparison) => comparison,
                _ => null
            };

            if(facetIri is null)
            {
                Report("Unknown facet.");

                continue;
            }

            if(ToLiteral(nodes[lo + 1]) is not Literal value)
            {
                Report("A facet value must be a literal.");

                continue;
            }

            facets.Add(new OwlFacetRestriction(new NamedNode(Utf8String.FromString(facetIri)), value));
        }

        return facets;
    }

    /// <summary>Skips leading <c>not</c> words; returns the index of the core.</summary>
    /// <param name="items">The items.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <returns>The first non-<c>not</c> index.</returns>
    private static int SkipNots(List<OwlManchesterNode> items, int lo, int hi)
    {
        while(lo < hi && IsWord(items, lo, "not"))
        {
            lo++;
        }

        return lo;
    }

    /// <summary>Splits a range at top-level occurrences of a separator word (and an optional synonym).</summary>
    /// <param name="items">The items.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <param name="word">The separator word.</param>
    /// <param name="synonym">An optional second separator treated identically.</param>
    /// <returns>The half-open part ranges.</returns>
    private static List<(int Lo, int Hi)> SplitAtWord(List<OwlManchesterNode> items, int lo, int hi, string word, string? synonym)
    {
        List<(int, int)> parts = [];
        int start = lo;
        for(int i = lo; i <= hi; i++)
        {
            if(i == hi || IsWord(items, i, word) || (synonym is not null && IsWord(items, i, synonym)))
            {
                parts.Add((start, i));
                start = i + 1;
            }
        }

        return parts;
    }

    private static bool IsWord(List<OwlManchesterNode> items, int index, string word)
    {
        return index < items.Count
            && items[index] is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } token }
            && token.Text == word;
    }

    private static bool IsComma(OwlManchesterNode node)
    {
        return node is { IsAtom: true, Atom.Kind: OwlManchesterTokenKind.Comma };
    }

    //Reference resolution and coercions.

    /// <summary>Resolves a node to an IRI without recording diagnostics, for lookahead decisions.</summary>
    /// <param name="node">The candidate node.</param>
    /// <returns>The IRI, or <see langword="null"/>.</returns>
    private Utf8String? PeekReference(OwlManchesterNode node)
    {
        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Iri } iri })
        {
            return Utf8String.FromString(iri.Text);
        }

        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } name } && TryResolveName(name.Text, out Utf8String resolved))
        {
            return resolved;
        }

        return null;
    }

    /// <summary>Resolves a node to an IRI, recording a diagnostic on failure.</summary>
    /// <param name="node">The candidate node.</param>
    /// <returns>The IRI, or <see langword="null"/> after a diagnostic.</returns>
    private Utf8String? ResolveReference(OwlManchesterNode node)
    {
        CurrentSpan = node.Span;

        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Iri } iri })
        {
            return Utf8String.FromString(iri.Text);
        }

        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } name })
        {
            if(OwlManchesterWords.Operators.Contains(name.Text) || OwlManchesterWords.Sections.Contains(name.Text) || OwlManchesterWords.IsFrameKeyword(name.Text))
            {
                Report($"'{name.Text}' is a reserved word, not a name.");

                return null;
            }

            if(TryResolveName(name.Text, out Utf8String resolved))
            {
                return resolved;
            }

            int colon = name.Text.IndexOf(':', System.StringComparison.Ordinal);
            Report(colon < 0
                ? "A bare name needs the default prefix; declare 'Prefix: : <iri>'."
                : $"Undeclared prefix '{name.Text[..colon]}:'.");

            return null;
        }

        Report("An IRI or a name was expected.");

        return null;
    }

    /// <summary>Resolves a name through the prefix table; a bare name uses the default prefix.</summary>
    /// <param name="name">The raw name text.</param>
    /// <param name="resolved">The resolved IRI on success.</param>
    /// <returns>Whether resolution succeeded.</returns>
    private bool TryResolveName(string name, out Utf8String resolved)
    {
        resolved = default!;

        int colon = name.IndexOf(':', System.StringComparison.Ordinal);
        string prefix = colon < 0 ? string.Empty : name[..colon];
        string local = colon < 0 ? name : name[(colon + 1)..];

        if(Prefixes.TryGetValue(prefix, out string? expansion))
        {
            resolved = Utf8String.FromString(expansion + local);

            return true;
        }

        return false;
    }

    /// <summary>Resolves a datatype node: a built-in abbreviation, a name, or an IRI.</summary>
    /// <param name="node">The candidate node.</param>
    /// <returns>The datatype IRI, or <see langword="null"/> after a diagnostic.</returns>
    private Utf8String? ResolveDatatype(OwlManchesterNode node)
    {
        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Name } name }
            && OwlManchesterWords.BuiltinDatatypes.TryGetValue(name.Text, out string? builtin))
        {
            return Utf8String.FromString(builtin);
        }

        return ResolveReference(node);
    }

    /// <summary>Coerces a node to an individual term: an IRI, a name, or a blank node.</summary>
    /// <param name="node">The candidate node.</param>
    /// <returns>The individual, or <see langword="null"/> after a diagnostic.</returns>
    private RdfTerm? ToIndividual(OwlManchesterNode node)
    {
        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.BlankNode } blank })
        {
            return new BlankNode(Utf8String.FromString(blank.Text));
        }

        return ResolveReference(node) is Utf8String iri ? new NamedNode(iri) : null;
    }

    /// <summary>Coerces a node to a literal: a quoted literal or a numeric lexical.</summary>
    /// <param name="node">The candidate node.</param>
    /// <returns>The literal, or <see langword="null"/> after a diagnostic.</returns>
    private Literal? ToLiteral(OwlManchesterNode node)
    {
        CurrentSpan = node.Span;

        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Literal } token })
        {
            if(token.LiteralLanguage is string language)
            {
                return new Literal(Utf8String.FromString(token.Text), new NamedNode(Vocabulary.Rdf.LangString), Utf8String.FromString(language));
            }

            Utf8String datatype = token.LiteralDatatype switch
            {
                null => Vocabulary.Xsd.String,
                ['<', .. var full] => Utf8String.FromString(full),
                _ when OwlManchesterWords.BuiltinDatatypes.TryGetValue(token.LiteralDatatype, out string? builtin) => Utf8String.FromString(builtin),
                _ => TryResolveName(token.LiteralDatatype, out Utf8String resolved) ? resolved : ReportDatatype(token.LiteralDatatype)
            };

            return new Literal(Utf8String.FromString(token.Text), new NamedNode(datatype));
        }

        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.Number } number })
        {
            return NumberToLiteral(number.Text);
        }

        Report("A literal was expected.");

        return null;
    }

    /// <summary>Reports an unresolvable literal datatype and falls back to <c>xsd:string</c>.</summary>
    /// <param name="datatype">The raw datatype text.</param>
    /// <returns>The fallback datatype IRI.</returns>
    private Utf8String ReportDatatype(string datatype)
    {
        Report($"Unresolvable datatype '{datatype}'.");

        return Vocabulary.Xsd.String;
    }

    /// <summary>Types a numeric lexical by its shape: an <c>f</c> suffix is a float, a fraction or exponent is a decimal, else an integer.</summary>
    /// <param name="lexical">The raw numeric lexical.</param>
    /// <returns>The typed literal.</returns>
    private static Literal NumberToLiteral(string lexical)
    {
        if(lexical.EndsWith('f') || lexical.EndsWith('F'))
        {
            return new Literal(Utf8String.FromString(lexical[..^1]), new NamedNode(Vocabulary.Xsd.Float));
        }

        bool fractional = lexical.Contains('.', System.StringComparison.Ordinal)
            || lexical.Contains('e', System.StringComparison.Ordinal)
            || lexical.Contains('E', System.StringComparison.Ordinal);

        return new Literal(
            Utf8String.FromString(lexical),
            new NamedNode(fractional ? Vocabulary.Xsd.Decimal : Vocabulary.Xsd.Integer));
    }

    /// <summary>Coerces a node to an annotation target: a literal, a numeric lexical, an IRI, a name, or a blank node.</summary>
    /// <param name="node">The candidate node.</param>
    /// <returns>The target term, or <see langword="null"/>.</returns>
    private RdfTerm? ToAnnotationValue(OwlManchesterNode node)
    {
        if(node is { IsAtom: true, Atom.Kind: OwlManchesterTokenKind.Literal or OwlManchesterTokenKind.Number })
        {
            return ToLiteral(node);
        }

        if(node is { IsAtom: true, Atom: { Kind: OwlManchesterTokenKind.BlankNode } blank })
        {
            return new BlankNode(Utf8String.FromString(blank.Text));
        }

        return PeekReference(node) is Utf8String iri ? new NamedNode(iri) : null;
    }

    /// <summary>Resolves a one-node range to an IRI, reporting when the range is not a single reference.</summary>
    /// <param name="items">The items.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <returns>The IRI, or <see langword="null"/> after a diagnostic.</returns>
    private Utf8String? SingleReference(List<OwlManchesterNode> items, int lo, int hi)
    {
        if(hi - lo != 1)
        {
            CurrentSpan = lo < items.Count && lo < hi ? items[lo].Span : CurrentSpan;
            Report("A single IRI or name was expected.");

            return null;
        }

        return ResolveReference(items[lo]);
    }

    /// <summary>Resolves a one-node range to an individual term.</summary>
    /// <param name="items">The items.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <returns>The individual, or <see langword="null"/> after a diagnostic.</returns>
    private RdfTerm? SingleIndividual(List<OwlManchesterNode> items, int lo, int hi)
    {
        if(hi - lo != 1)
        {
            CurrentSpan = lo < items.Count && lo < hi ? items[lo].Span : CurrentSpan;
            Report("A single individual was expected.");

            return null;
        }

        return ToIndividual(items[lo]);
    }

    /// <summary>Coerces a converted expression value to a class expression.</summary>
    /// <param name="value">The converted value.</param>
    /// <returns>The class expression, an invalid reference standing in for a failed conversion.</returns>
    private static OwlClassExpression AsClass(object? value)
    {
        return value switch
        {
            OwlClassExpression expression => expression,
            _ => new OwlClassReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")))
        };
    }

    /// <summary>Coerces a converted expression value to a data range.</summary>
    /// <param name="value">The converted value.</param>
    /// <returns>The data range, an invalid reference standing in for a failed conversion.</returns>
    private static OwlDataRange AsDataRange(object? value)
    {
        return value switch
        {
            OwlDataRange range => range,
            _ => new OwlDatatypeReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")))
        };
    }

    /// <summary>Reports a section keyword that does not belong to the current frame kind.</summary>
    /// <param name="keyword">The offending keyword token.</param>
    private void ReportUnexpectedSection(OwlManchesterToken keyword)
    {
        Report($"The section '{keyword.Text}' does not belong to this frame.");
    }

    /// <summary>Records an error diagnostic at the current span.</summary>
    /// <param name="message">The diagnostic message.</param>
    private void Report(string message)
    {
        Diagnostics.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            CurrentSpan,
            Utf8String.FromString(message)));
    }
}
