using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Iris;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Ast;

namespace Lumoin.Veritas.Turtle.Emission;

/// <summary>
/// Walks a <see cref="TurtleDocument"/> AST and yields <see cref="EmittedQuad"/>
/// instances. Each emitted quad carries an optional
/// <see cref="DocumentNodeRef"/> pointing back at the AST node it
/// originated from, enabling editor and provenance consumers to follow
/// the link.
/// </summary>
/// <remarks>
/// <para>
/// Prefix expansion and base IRI resolution happen here, not in the
/// parser, so the AST preserves the surface form. Collections expand
/// into <c>rdf:first</c> / <c>rdf:rest</c> / <c>rdf:nil</c> chains.
/// Blank-node property lists expand into per-predicate triples sharing
/// the freshly-allocated blank-node subject. Triple terms become
/// <see cref="Core.TripleTerm"/> values; reified triples produce both
/// the inner triple and the reification statement.
/// </para>
/// </remarks>
public sealed class TurtleQuadEmitter
{
    private string? baseIri;
    private int nextEmitterBlankNode;

    /// <summary>
    /// Initialises a new <see cref="TurtleQuadEmitter"/>.
    /// </summary>
    /// <param name="document">The parsed Turtle or TriG document.</param>
    /// <param name="pool">The pool used to intern emitter-allocated identifiers.</param>
    /// <param name="documentBase">
    /// The document's retrieval IRI, used as the initial base for resolving relative references
    /// that appear before any in-document <c>@base</c> directive. A later <c>@base</c> resolves
    /// against it. When <see langword="null"/>, only an in-document <c>@base</c> establishes a base.
    /// </param>
    public TurtleQuadEmitter(TurtleDocument document, Utf8StringPool pool, string? documentBase = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pool);

        Document = document;
        Pool = pool;
        baseIri = documentBase;
    }

    /// <summary>Gets the parsed document being walked.</summary>
    private TurtleDocument Document { get; }

    /// <summary>Gets the pool used to intern emitter-allocated identifiers.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>Gets the prefix-to-namespace map accumulated from the document's prefix declarations.</summary>
    private Dictionary<Utf8String, IriTerm> PrefixMap { get; } = [];

    /// <summary>
    /// Walks the document and yields <see cref="EmittedQuad"/> instances in
    /// source order.
    /// </summary>
    /// <returns>An iterator over the quads the document expresses.</returns>
    public IEnumerable<EmittedQuad> Emit()
    {
        foreach(Statement statement in Document.Statements)
        {
            foreach(EmittedQuad emitted in EmitStatement(statement))
            {
                yield return emitted;
            }
        }
    }

    /// <summary>
    /// Emits the quads of a single statement, updating the accumulating prefix-map and base-IRI
    /// context so a streaming driver can feed statements one at a time as they are parsed.
    /// </summary>
    /// <remarks>
    /// A prefix or base declaration yields no quads but updates the context that later statements
    /// resolve against; a version declaration is informational. Call statements in source order:
    /// the context is stateful, so a prefixed name resolves only against prefixes declared before it.
    /// </remarks>
    /// <param name="statement">The statement to emit.</param>
    /// <returns>The quads the statement expresses, in source order.</returns>
    internal IEnumerable<EmittedQuad> EmitStatement(Statement statement)
    {
        switch(statement)
        {
            case PrefixDeclaration prefix:
            {
                PrefixMap[prefix.Prefix] = prefix.Iri;

                break;
            }

            case BaseDeclaration baseDecl:
            {
                //A @base directive may itself be a relative reference; it resolves against the
                //base already in scope per RFC 3986 §5, so chained directives compose.
                string declared = baseDecl.Iri.Value.ToString();
                baseIri = baseIri is null
                    ? declared
                    : IriResolver.ResolveIri(baseIri, declared);

                break;
            }

            case VersionDeclaration:
            {
                //Informational only; not surfaced as a quad.
                break;
            }

            case TripleStatement triple:
            {
                foreach(EmittedQuad q in EmitTriple(triple, graph: null))
                {
                    yield return q;
                }

                break;
            }

            case GraphBlockStatement graphBlock:
            {
                RdfTerm? graphTerm = graphBlock.Label is null
                    ? null
                    : ResolveTermAsSimple(graphBlock.Label);

                foreach(TripleStatement inner in graphBlock.Triples)
                {
                    foreach(EmittedQuad q in EmitTriple(inner, graphTerm))
                    {
                        yield return q;
                    }
                }

                break;
            }

            default:
            {
                break;
            }
        }
    }

    private IEnumerable<EmittedQuad> EmitTriple(TripleStatement triple, RdfTerm? graph)
    {
        RdfTerm subject = ResolveTerm(triple.Subject, graph, out List<EmittedQuad> subjectAux);
        foreach(EmittedQuad q in subjectAux)
        {
            yield return q;
        }

        foreach(PredicateObject predObj in triple.Predicates)
        {
            NamedNode predicate = ResolvePredicate(predObj.Predicate);
            foreach(AnnotatedObject annotatedObject in predObj.Objects)
            {
                RdfTerm objectTerm = ResolveTerm(annotatedObject.Object, graph, out List<EmittedQuad> objectAux);
                foreach(EmittedQuad q in objectAux)
                {
                    yield return q;
                }

                Quad mainQuad = new(subject, predicate, objectTerm, graph);

                yield return new EmittedQuad(mainQuad, new DocumentNodeRef(Document.DocumentId, annotatedObject.NodeId));

                if(!annotatedObject.Annotations.IsDefaultOrEmpty)
                {
                    foreach(EmittedQuad q in EmitAnnotations(annotatedObject, subject, predicate, objectTerm, graph))
                    {
                        yield return q;
                    }
                }
            }
        }
    }

    private IEnumerable<EmittedQuad> EmitAnnotations(
        AnnotatedObject annotatedObject,
        RdfTerm subject,
        NamedNode predicate,
        RdfTerm objectTerm,
        RdfTerm? graph)
    {
        //Per RDF 1.2 each reifier and each annotation block produces a reifying triple
        //"reifier rdf:reifies <<( s p o )>>". A reifier marker (~ with an optional identifier)
        //leaves its reifier pending so an immediately-following annotation block reuses it;
        //a block with no pending reifier allocates a fresh blank node. A block consumes the
        //pending reifier, so consecutive blocks each get their own reifier. Annotation blocks
        //nest — a block attached to an object inside another block annotates that outer block's
        //annotation triple — and the nesting is walked with an explicit stack rather than by
        //recursion.
        NamedNode reifies = new(Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8));
        Stack<AnnotationFrame> work = new();
        work.Push(new AnnotationFrame(annotatedObject, subject, predicate, objectTerm));

        while(work.Count > 0)
        {
            AnnotationFrame frame = work.Pop();
            TripleTerm tripleTerm = new(frame.Subject, frame.Predicate, frame.ObjectTerm);
            RdfTerm? pendingReifier = null;

            foreach(Annotation annotation in frame.AnnotatedObject.Annotations)
            {
                switch(annotation)
                {
                    case ReifierAnnotation reifier:
                    {
                        RdfTerm resolvedReifier = reifier.Identifier is null
                            ? AllocateBlankNode()
                            : ResolveTermAsSimple(reifier.Identifier);

                        Quad reificationQuad = new(resolvedReifier, reifies, tripleTerm, graph);

                        yield return new EmittedQuad(reificationQuad, new DocumentNodeRef(Document.DocumentId, reifier.NodeId));
                        pendingReifier = resolvedReifier;

                        break;
                    }

                    case AnnotationBlock block:
                    {
                        RdfTerm blockReifier;
                        if(pendingReifier is not null)
                        {
                            blockReifier = pendingReifier;
                        }
                        else
                        {
                            blockReifier = AllocateBlankNode();
                            Quad reificationQuad = new(blockReifier, reifies, tripleTerm, graph);

                            yield return new EmittedQuad(reificationQuad, new DocumentNodeRef(Document.DocumentId, block.NodeId));
                        }

                        foreach(PredicateObject inner in block.Predicates)
                        {
                            NamedNode innerPredicate = ResolvePredicate(inner.Predicate);
                            foreach(AnnotatedObject innerObject in inner.Objects)
                            {
                                RdfTerm innerObjectTerm = ResolveTerm(innerObject.Object, graph, out List<EmittedQuad> aux);
                                foreach(EmittedQuad q in aux)
                                {
                                    yield return q;
                                }

                                Quad annotationQuad = new(blockReifier, innerPredicate, innerObjectTerm, graph);

                                yield return new EmittedQuad(annotationQuad, new DocumentNodeRef(Document.DocumentId, innerObject.NodeId));

                                //An annotation nested on an object within the block annotates the
                                //block's own annotation triple (blockReifier innerPredicate innerObject).
                                if(!innerObject.Annotations.IsDefaultOrEmpty)
                                {
                                    work.Push(new AnnotationFrame(innerObject, blockReifier, innerPredicate, innerObjectTerm));
                                }
                            }
                        }

                        pendingReifier = null;

                        break;
                    }

                    default:
                    {
                        break;
                    }
                }
            }
        }
    }

    private RdfTerm ResolveTermAsSimple(Term term)
    {
        return ResolveTerm(term, graph: null, out _);
    }

    private RdfTerm ResolveTerm(Term term, RdfTerm? graph, out List<EmittedQuad> auxiliary)
    {
        List<EmittedQuad> sink = [];
        auxiliary = sink;

        //Resolve the term tree bottom-up with an explicit stack: a compound term is combined
        //only after its child terms are resolved, so no production resolves another by recursion.
        //Auxiliary quads accumulate into the shared sink; each child's quads are appended when the
        //child is combined, which is before its parent — so order differs from a top-down walk, but
        //the emitted set is the same.
        Dictionary<int, RdfTerm> resolved = [];
        Stack<ResolveFrame> stack = new();
        stack.Push(new ResolveFrame(term, Expanded: false));

        while(stack.Count > 0)
        {
            ResolveFrame frame = stack.Pop();
            Term current = frame.Term;

            if(frame.Expanded)
            {
                resolved[current.NodeId] = CombineResolvedTerm(current, graph, resolved, sink);

                continue;
            }

            switch(current)
            {
                case IriTerm iri:
                {
                    resolved[current.NodeId] = new NamedNode(ResolveIri(iri.Value, iri.Span));

                    break;
                }

                case PrefixedNameTerm prefixed:
                {
                    resolved[current.NodeId] = new NamedNode(ExpandPrefixedName(prefixed));

                    break;
                }

                case BlankNodeTerm blank:
                {
                    resolved[current.NodeId] = new BlankNode(blank.Label);

                    break;
                }

                case LiteralTerm literal:
                {
                    resolved[current.NodeId] = BuildLiteral(literal);

                    break;
                }

                default:
                {
                    //A compound term is revisited (Expanded) once its children are resolved.
                    stack.Push(new ResolveFrame(current, Expanded: true));
                    PushResolveChildren(current, stack);

                    break;
                }
            }
        }

        return resolved[term.NodeId];
    }

    private static void PushResolveChildren(Term term, Stack<ResolveFrame> stack)
    {
        switch(term)
        {
            case CollectionTerm collection:
            {
                foreach(Term item in collection.Items)
                {
                    stack.Push(new ResolveFrame(item, Expanded: false));
                }

                break;
            }

            case BlankNodePropertyListTerm propertyList:
            {
                foreach(PredicateObject predObj in propertyList.Predicates)
                {
                    foreach(AnnotatedObject annotatedObject in predObj.Objects)
                    {
                        stack.Push(new ResolveFrame(annotatedObject.Object, Expanded: false));
                    }
                }

                break;
            }

            case TripleTermTerm tripleTerm:
            {
                stack.Push(new ResolveFrame(tripleTerm.Subject, Expanded: false));
                stack.Push(new ResolveFrame(tripleTerm.Object, Expanded: false));

                break;
            }

            case ReifiedTripleTerm reified:
            {
                stack.Push(new ResolveFrame(reified.Subject, Expanded: false));
                stack.Push(new ResolveFrame(reified.Object, Expanded: false));
                if(reified.Reifier is not null)
                {
                    stack.Push(new ResolveFrame(reified.Reifier, Expanded: false));
                }

                break;
            }

            default:
            {
                throw new TurtleParseException(
                    $"Unsupported term kind {term.GetType().Name}.",
                    term.Span);
            }
        }
    }

    private RdfTerm CombineResolvedTerm(
        Term term,
        RdfTerm? graph,
        Dictionary<int, RdfTerm> resolved,
        List<EmittedQuad> sink)
    {
        switch(term)
        {
            case CollectionTerm collection:
            {
                return BuildCollection(collection, graph, resolved, sink);
            }

            case BlankNodePropertyListTerm propertyList:
            {
                BlankNode head = AllocateBlankNode();
                EmitPropertyListBody(head, propertyList, graph, resolved, sink);

                return head;
            }

            case TripleTermTerm tripleTermNode:
            {
                RdfTerm inner = resolved[tripleTermNode.Subject.NodeId];
                NamedNode innerPredicate = ResolvePredicate(tripleTermNode.Predicate);
                RdfTerm innerObject = resolved[tripleTermNode.Object.NodeId];

                return new TripleTerm(inner, innerPredicate, innerObject);
            }

            case ReifiedTripleTerm reified:
            {
                return ExpandReifiedTriple(reified, graph, resolved, sink);
            }

            default:
            {
                throw new TurtleParseException(
                    $"Unsupported term kind {term.GetType().Name}.",
                    term.Span);
            }
        }
    }

    private void EmitPropertyListBody(
        BlankNode head,
        BlankNodePropertyListTerm propertyList,
        RdfTerm? graph,
        Dictionary<int, RdfTerm> resolved,
        List<EmittedQuad> sink)
    {
        DocumentNodeRef anchor = new(Document.DocumentId, propertyList.NodeId);

        foreach(PredicateObject predObj in propertyList.Predicates)
        {
            NamedNode predicate = ResolvePredicate(predObj.Predicate);
            foreach(AnnotatedObject annotatedObject in predObj.Objects)
            {
                RdfTerm obj = resolved[annotatedObject.Object.NodeId];

                Quad quad = new(head, predicate, obj, graph);
                sink.Add(new EmittedQuad(quad, new DocumentNodeRef(Document.DocumentId, annotatedObject.NodeId)));

                if(!annotatedObject.Annotations.IsDefaultOrEmpty)
                {
                    foreach(EmittedQuad q in EmitAnnotations(annotatedObject, head, predicate, obj, graph))
                    {
                        sink.Add(q);
                    }
                }
            }
        }

        //The anchor is kept on the propertyList node itself; nothing more to emit at the head level.
        _ = anchor;
    }

    private RdfTerm BuildCollection(
        CollectionTerm collection,
        RdfTerm? graph,
        Dictionary<int, RdfTerm> resolved,
        List<EmittedQuad> sink)
    {
        NamedNode rdfNil = new(Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8));
        if(collection.Items.IsDefaultOrEmpty)
        {
            return rdfNil;
        }

        NamedNode rdfFirst = new(Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8));
        NamedNode rdfRest = new(Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8));

        BlankNode head = AllocateBlankNode();
        BlankNode currentHead = head;

        for(int i = 0; i < collection.Items.Length; i++)
        {
            RdfTerm item = resolved[collection.Items[i].NodeId];

            sink.Add(new EmittedQuad(
                new Quad(currentHead, rdfFirst, item, graph),
                new DocumentNodeRef(Document.DocumentId, collection.NodeId)));

            if(i == collection.Items.Length - 1)
            {
                sink.Add(new EmittedQuad(
                    new Quad(currentHead, rdfRest, rdfNil, graph),
                    new DocumentNodeRef(Document.DocumentId, collection.NodeId)));
            }
            else
            {
                BlankNode next = AllocateBlankNode();
                sink.Add(new EmittedQuad(
                    new Quad(currentHead, rdfRest, next, graph),
                    new DocumentNodeRef(Document.DocumentId, collection.NodeId)));
                currentHead = next;
            }
        }

        return head;
    }

    private RdfTerm ExpandReifiedTriple(
        ReifiedTripleTerm reified,
        RdfTerm? graph,
        Dictionary<int, RdfTerm> resolved,
        List<EmittedQuad> sink)
    {
        RdfTerm subject = resolved[reified.Subject.NodeId];
        NamedNode predicate = ResolvePredicate(reified.Predicate);
        RdfTerm objectTerm = resolved[reified.Object.NodeId];

        //A reified triple <<s p o>> yields only the reifier and the reification
        //"reifier rdf:reifies <<( s p o )>>"; it does not assert the inner triple. The inner
        //triple is asserted only when written as a plain statement (optionally annotated).
        RdfTerm reifier = reified.Reifier is null
            ? AllocateBlankNode()
            : resolved[reified.Reifier.NodeId];

        NamedNode reifies = new(Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8));
        TripleTerm tripleTerm = new(subject, predicate, objectTerm);

        sink.Add(new EmittedQuad(
            new Quad(reifier, reifies, tripleTerm, graph),
            new DocumentNodeRef(Document.DocumentId, reified.NodeId)));

        return reifier;
    }

    private NamedNode ResolvePredicate(Term predicate)
    {
        return predicate switch
        {
            IriTerm iri => new NamedNode(ResolveIri(iri.Value, iri.Span)),
            PrefixedNameTerm prefixed => new NamedNode(ExpandPrefixedName(prefixed)),
            _ => throw new TurtleParseException(
                $"Predicate must be an IRI or prefixed name, was {predicate.GetType().Name}.",
                predicate.Span)
        };
    }

    private Literal BuildLiteral(LiteralTerm literal)
    {
        NamedNode datatype;
        if(literal.Datatype is IriTerm iriDatatype)
        {
            datatype = new NamedNode(ResolveIri(iriDatatype.Value, iriDatatype.Span));
        }
        else if(literal.Datatype is PrefixedNameTerm prefixedDatatype)
        {
            datatype = new NamedNode(ExpandPrefixedName(prefixedDatatype));
        }
        else if(literal.Language is not null && literal.Direction is not null)
        {
            datatype = new NamedNode(Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString"u8));
        }
        else if(literal.Language is not null)
        {
            datatype = new NamedNode(Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8));
        }
        else
        {
            datatype = new NamedNode(Pool.Intern("http://www.w3.org/2001/XMLSchema#string"u8));
        }

        return new Literal(literal.Value, datatype, literal.Language, literal.Direction);
    }

    private Utf8String ResolveIri(Utf8String value, SourceSpan span)
    {
        //Absolute IRIs pass through without allocating; only a relative reference needs resolving.
        ReadOnlySpan<byte> bytes = value.Span;
        if(IsAbsoluteIri(bytes))
        {
            return value;
        }

        //A relative reference is resolved against the in-scope base (the document base, then any
        //@base directive) per RFC 3986 §5. RDF admits only absolute IRIs in term positions, so a
        //reference that stays relative — because no base is in scope, or the base itself is not
        //absolute — cannot be expressed and is rejected.
        if(baseIri is not null)
        {
            string resolved = IriResolver.ResolveIri(baseIri, value.ToString());
            if(IriResolver.IsAbsoluteIri(resolved))
            {
                return Pool.Intern(resolved);
            }
        }

        throw new TurtleParseException(
            $"Relative IRI '{value.ToString()}' cannot be resolved to an absolute IRI; no absolute @base or document base is in scope.",
            span);
    }

    private Utf8String ExpandPrefixedName(PrefixedNameTerm prefixed)
    {
        if(!PrefixMap.TryGetValue(prefixed.Prefix, out IriTerm? namespaceIri))
        {
            throw new TurtleParseException(
                $"Undeclared prefix '{prefixed.Prefix.ToString()}' in prefixed name.",
                prefixed.Span);
        }

        ReadOnlySpan<byte> nsBytes = namespaceIri.Value.Span;
        ReadOnlySpan<byte> localBytes = prefixed.Local.Span;
        int total = nsBytes.Length + localBytes.Length;
        Span<byte> buffer = total <= 1024
            ? stackalloc byte[total]
            : new byte[total];

        nsBytes.CopyTo(buffer);
        localBytes.CopyTo(buffer[nsBytes.Length..]);

        return Pool.Intern(buffer);
    }

    private static bool IsAbsoluteIri(ReadOnlySpan<byte> bytes)
    {
        //An IRI is absolute when it starts with an ASCII-letter and the next non-empty prefix forms scheme:.
        for(int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if(b == (byte)':')
            {
                return i > 0;
            }

            bool ok = i == 0
                ? (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z')
                : (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'0' && b <= (byte)'9') || b == (byte)'+' || b == (byte)'-' || b == (byte)'.';

            if(!ok)
            {
                return false;
            }
        }

        return false;
    }

    private BlankNode AllocateBlankNode()
    {
        string label = string.Create(CultureInfo.InvariantCulture, $"g{nextEmitterBlankNode}");
        nextEmitterBlankNode++;

        return new BlankNode(Pool.Intern(label));
    }
}
