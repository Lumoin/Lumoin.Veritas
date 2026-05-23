using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
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
    private readonly TurtleDocument document;
    private readonly Utf8StringPool pool;
    private readonly Dictionary<Utf8String, IriTerm> prefixMap = [];
    private IriTerm? baseIri;
    private int nextEmitterBlankNode;

    /// <summary>
    /// Initialises a new <see cref="TurtleQuadEmitter"/>.
    /// </summary>
    /// <param name="document">The parsed Turtle or TriG document.</param>
    /// <param name="pool">The pool used to intern emitter-allocated identifiers.</param>
    public TurtleQuadEmitter(TurtleDocument document, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pool);

        this.document = document;
        this.pool = pool;
    }

    /// <summary>
    /// Walks the document and yields <see cref="EmittedQuad"/> instances in
    /// source order.
    /// </summary>
    /// <returns>An iterator over the quads the document expresses.</returns>
    public IEnumerable<EmittedQuad> Emit()
    {
        foreach(Statement statement in document.Statements)
        {
            switch(statement)
            {
                case PrefixDeclaration prefix:
                {
                    prefixMap[prefix.Prefix] = prefix.Iri;
                    break;
                }

                case BaseDeclaration baseDecl:
                {
                    baseIri = baseDecl.Iri;
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
                yield return new EmittedQuad(mainQuad, new DocumentNodeRef(document.DocumentId, annotatedObject.NodeId));

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
        //Per RDF 1.2: every reifier or annotation block produces one
        //reifying triple "reifier rdf:reifies <<( s p o )>>", with the
        //annotation block additionally asserting its predicate-object
        //list with the reifier as subject.
        TripleTerm tripleTerm = new(subject, predicate, objectTerm);
        NamedNode reifies = new(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8));
        RdfTerm? currentReifier = null;

        foreach(Annotation annotation in annotatedObject.Annotations)
        {
            switch(annotation)
            {
                case ReifierAnnotation reifier:
                {
                    currentReifier = reifier.Identifier is null
                        ? AllocateBlankNode()
                        : ResolveTermAsSimple(reifier.Identifier);

                    Quad reificationQuad = new(currentReifier, reifies, tripleTerm, graph);
                    yield return new EmittedQuad(reificationQuad, new DocumentNodeRef(document.DocumentId, reifier.NodeId));
                    break;
                }

                case AnnotationBlock block:
                {
                    if(currentReifier is null)
                    {
                        currentReifier = AllocateBlankNode();
                        Quad reificationQuad = new(currentReifier, reifies, tripleTerm, graph);
                        yield return new EmittedQuad(reificationQuad, new DocumentNodeRef(document.DocumentId, block.NodeId));
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

                            Quad annotationQuad = new(currentReifier, innerPredicate, innerObjectTerm, graph);
                            yield return new EmittedQuad(annotationQuad, new DocumentNodeRef(document.DocumentId, innerObject.NodeId));
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
    }

    private RdfTerm ResolveTermAsSimple(Term term)
    {
        return ResolveTerm(term, graph: null, out _);
    }

    private RdfTerm ResolveTerm(Term term, RdfTerm? graph, out List<EmittedQuad> auxiliary)
    {
        auxiliary = [];

        switch(term)
        {
            case IriTerm iri:
            {
                return new NamedNode(ResolveIri(iri.Value));
            }

            case PrefixedNameTerm prefixed:
            {
                return new NamedNode(ExpandPrefixedName(prefixed));
            }

            case BlankNodeTerm blank:
            {
                return new BlankNode(blank.Label);
            }

            case LiteralTerm literal:
            {
                return BuildLiteral(literal);
            }

            case CollectionTerm collection:
            {
                return BuildCollection(collection, graph, auxiliary);
            }

            case BlankNodePropertyListTerm propertyList:
            {
                BlankNode head = AllocateBlankNode();
                EmitPropertyListBody(head, propertyList, graph, auxiliary);
                return head;
            }

            case TripleTermTerm tripleTermNode:
            {
                RdfTerm inner = ResolveTerm(tripleTermNode.Subject, graph, out List<EmittedQuad> subjAux);
                NamedNode innerPredicate = ResolvePredicate(tripleTermNode.Predicate);
                RdfTerm innerObject = ResolveTerm(tripleTermNode.Object, graph, out List<EmittedQuad> objAux);
                AppendTo(auxiliary, subjAux);
                AppendTo(auxiliary, objAux);
                return new TripleTerm(inner, innerPredicate, innerObject);
            }

            case ReifiedTripleTerm reified:
            {
                return ExpandReifiedTriple(reified, graph, auxiliary);
            }

            default:
            {
                throw new TurtleParseException(
                    $"Unsupported term kind {term.GetType().Name}.",
                    term.Span);
            }
        }
    }

    private static void AppendTo(List<EmittedQuad> target, List<EmittedQuad> source)
    {
        for(int i = 0; i < source.Count; i++)
        {
            target.Add(source[i]);
        }
    }

    private void EmitPropertyListBody(
        BlankNode head,
        BlankNodePropertyListTerm propertyList,
        RdfTerm? graph,
        List<EmittedQuad> sink)
    {
        DocumentNodeRef anchor = new(document.DocumentId, propertyList.NodeId);

        foreach(PredicateObject predObj in propertyList.Predicates)
        {
            NamedNode predicate = ResolvePredicate(predObj.Predicate);
            foreach(AnnotatedObject annotatedObject in predObj.Objects)
            {
                RdfTerm obj = ResolveTerm(annotatedObject.Object, graph, out List<EmittedQuad> objAux);
                AppendTo(sink, objAux);

                Quad quad = new(head, predicate, obj, graph);
                sink.Add(new EmittedQuad(quad, new DocumentNodeRef(document.DocumentId, annotatedObject.NodeId)));

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

    private RdfTerm BuildCollection(CollectionTerm collection, RdfTerm? graph, List<EmittedQuad> sink)
    {
        NamedNode rdfNil = new(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8));
        if(collection.Items.IsDefaultOrEmpty)
        {
            return rdfNil;
        }

        NamedNode rdfFirst = new(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8));
        NamedNode rdfRest = new(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8));

        BlankNode head = AllocateBlankNode();
        BlankNode currentHead = head;

        for(int i = 0; i < collection.Items.Length; i++)
        {
            RdfTerm item = ResolveTerm(collection.Items[i], graph, out List<EmittedQuad> itemAux);
            AppendTo(sink, itemAux);

            sink.Add(new EmittedQuad(
                new Quad(currentHead, rdfFirst, item, graph),
                new DocumentNodeRef(document.DocumentId, collection.NodeId)));

            if(i == collection.Items.Length - 1)
            {
                sink.Add(new EmittedQuad(
                    new Quad(currentHead, rdfRest, rdfNil, graph),
                    new DocumentNodeRef(document.DocumentId, collection.NodeId)));
            }
            else
            {
                BlankNode next = AllocateBlankNode();
                sink.Add(new EmittedQuad(
                    new Quad(currentHead, rdfRest, next, graph),
                    new DocumentNodeRef(document.DocumentId, collection.NodeId)));
                currentHead = next;
            }
        }

        return head;
    }

    private RdfTerm ExpandReifiedTriple(ReifiedTripleTerm reified, RdfTerm? graph, List<EmittedQuad> sink)
    {
        RdfTerm subject = ResolveTerm(reified.Subject, graph, out List<EmittedQuad> subjAux);
        NamedNode predicate = ResolvePredicate(reified.Predicate);
        RdfTerm objectTerm = ResolveTerm(reified.Object, graph, out List<EmittedQuad> objAux);
        AppendTo(sink, subjAux);
        AppendTo(sink, objAux);

        //Per RDF 1.2 the reified triple is asserted as a triple AND a reification.
        sink.Add(new EmittedQuad(
            new Quad(subject, predicate, objectTerm, graph),
            new DocumentNodeRef(document.DocumentId, reified.NodeId)));

        RdfTerm reifier = reified.Reifier is null
            ? AllocateBlankNode()
            : ResolveTermAsSimple(reified.Reifier);

        NamedNode reifies = new(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8));
        TripleTerm tripleTerm = new(subject, predicate, objectTerm);

        sink.Add(new EmittedQuad(
            new Quad(reifier, reifies, tripleTerm, graph),
            new DocumentNodeRef(document.DocumentId, reified.NodeId)));

        return reifier;
    }

    private NamedNode ResolvePredicate(Term predicate)
    {
        return predicate switch
        {
            IriTerm iri => new NamedNode(ResolveIri(iri.Value)),
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
            datatype = new NamedNode(ResolveIri(iriDatatype.Value));
        }
        else if(literal.Datatype is PrefixedNameTerm prefixedDatatype)
        {
            datatype = new NamedNode(ExpandPrefixedName(prefixedDatatype));
        }
        else if(literal.Language is not null && literal.Direction is not null)
        {
            datatype = new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString"u8));
        }
        else if(literal.Language is not null)
        {
            datatype = new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8));
        }
        else
        {
            datatype = new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"u8));
        }

        return new Literal(literal.Value, datatype, literal.Language, literal.Direction);
    }

    private Utf8String ResolveIri(Utf8String value)
    {
        //If the value is absolute (scheme present), use as-is. Otherwise resolve against the base IRI if one is set.
        ReadOnlySpan<byte> bytes = value.Span;
        if(IsAbsoluteIri(bytes))
        {
            return value;
        }

        if(baseIri is null)
        {
            return value;
        }

        ReadOnlySpan<byte> baseBytes = baseIri.Value.Span;
        Span<byte> buffer = bytes.Length + baseBytes.Length <= 1024
            ? stackalloc byte[bytes.Length + baseBytes.Length]
            : new byte[bytes.Length + baseBytes.Length];

        baseBytes.CopyTo(buffer);
        bytes.CopyTo(buffer[baseBytes.Length..]);
        return pool.Intern(buffer);
    }

    private Utf8String ExpandPrefixedName(PrefixedNameTerm prefixed)
    {
        if(!prefixMap.TryGetValue(prefixed.Prefix, out IriTerm? namespaceIri))
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
        return pool.Intern(buffer);
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
        return new BlankNode(pool.Intern(label));
    }
}
