using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Sparql.Ast;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;

namespace Lumoin.Veritas.Sparql.Translation;

/// <summary>
/// Lowers the RDF 1.2 syntactic sugar in a parsed SPARQL request to plain triple patterns, the
/// early-expansion pass that runs between parsing and algebra translation. After normalization every
/// <see cref="BasicGraphPatternBlock"/> (and every <see cref="ConstructQuery"/> template) holds only
/// plain <see cref="TriplePattern"/>s whose terms are the four core cases —
/// <see cref="ConstantTerm"/>, <see cref="VariableTerm"/>, <see cref="PropertyPathTerm"/> (predicate
/// only), and <see cref="AstTripleTerm"/> (a quoted triple term in subject/object position) — plus
/// <see cref="ErrorTriplePatternTerm"/> placeholders carried through unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The lowering mirrors <c>Lumoin.Veritas.Turtle.Emission.TurtleQuadEmitter</c> — collections expand to
/// <c>rdf:first</c>/<c>rdf:rest</c>/<c>rdf:nil</c> chains, blank-node property lists to a fresh blank
/// node carrying per-predicate triples, reified triples to a <c>rdf:reifies</c> reification triple, and
/// annotations to reification triples about the annotated triple's reifier — but differs in four ways:
/// SPARQL terms include variables (preserved, never resolved); no prefix/IRI resolution happens here
/// (a <see cref="ConstantTerm"/> already carries its resolved term from parsing); the output is the
/// <see cref="TriplePattern"/> AST, not encoded quads; and a predicate may be a property path, which is
/// left untouched.
/// </para>
/// <para>
/// Fresh blank nodes come from the same <see cref="BlankNodeDelegate"/> seam the parser uses (default
/// <see cref="VeritasBlankNodes.System"/>). Threading the same <see cref="Utf8StringPool"/> the parse
/// used keeps the per-pool counter monotonic, so normalizer-allocated labels never collide with the
/// parser's anonymous <c>[]</c> labels. Source spans are preserved onto every lowered triple from the
/// sugar node that produced it. The pass never throws on a recovered (error-node) AST — error nodes
/// carry their own diagnostics and flow through untouched.
/// </para>
/// </remarks>
public sealed class SparqlNormalizer
{
    private readonly Utf8StringPool pool;
    private readonly BlankNodeDelegate blankNodes;
    private readonly SparqlNormalizerOptions options;

    /// <summary>
    /// Initialises a new <see cref="SparqlNormalizer"/>.
    /// </summary>
    /// <param name="pool">The interning pool; pass the same pool the parse used so fresh blank-node labels do not collide.</param>
    /// <param name="blankNodes">Allocates labels for normalizer-created blank nodes; defaults to <see cref="VeritasBlankNodes.System"/>.</param>
    /// <param name="options">The lowering options; defaults to <see cref="SparqlNormalizerOptions.Default"/> (spec-faithful).</param>
    public SparqlNormalizer(Utf8StringPool pool, BlankNodeDelegate? blankNodes = null, SparqlNormalizerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        this.pool = pool;
        this.blankNodes = blankNodes ?? VeritasBlankNodes.System;
        this.options = options ?? SparqlNormalizerOptions.Default;
    }

    /// <summary>
    /// Normalizes a parsed request, lowering all RDF 1.2 sugar in its query forms and graph patterns.
    /// </summary>
    /// <param name="request">The parsed request.</param>
    /// <returns>The request with sugar lowered to plain triple patterns; an update request is returned unchanged.</returns>
    public SparqlRequest Normalize(SparqlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request switch
        {
            SparqlQuery query => NormalizeQuery(query),
            _ => request
        };
    }

    /// <summary>Normalizes a query: its CONSTRUCT template (if any) and its WHERE pattern.</summary>
    /// <param name="query">The query to normalize.</param>
    /// <returns>The normalized query.</returns>
    private SparqlQuery NormalizeQuery(SparqlQuery query)
    {
        QueryForm form = query.Form is ConstructQuery construct
            ? new ConstructQuery(construct.Span, NormalizeTemplate(construct.Template))
            : query.Form;

        WhereClause where = query.Where with { Pattern = NormalizePattern(query.Where.Pattern) };

        return query with { Form = form, Where = where };
    }

    /// <summary>Lowers the triples of a CONSTRUCT template.</summary>
    /// <param name="template">The template triples.</param>
    /// <returns>The flattened, lowered template triples.</returns>
    private List<TriplePattern> NormalizeTemplate(IReadOnlyList<TriplePattern> template)
    {
        List<TriplePattern> result = [];
        foreach(TriplePattern triple in template)
        {
            LowerTriple(triple, result);
        }

        return result;
    }

    /// <summary>
    /// Normalizes a graph pattern, rebuilding each basic block with lowered triples and recursing into nested
    /// groups — over an explicit post-order stack (no recursion), so arbitrarily deep pattern nesting cannot
    /// overflow the stack. Each pattern's normalized sub-patterns are looked up by reference when it is rebuilt.
    /// </summary>
    /// <param name="root">The pattern to normalize.</param>
    /// <returns>The normalized pattern.</returns>
    private GraphPattern NormalizePattern(GraphPattern root)
    {
        //Keyed by reference: a parsed pattern is a tree, and value-equal sibling patterns are distinct
        //positions that must map to their own normalized result.
        Dictionary<GraphPattern, GraphPattern> normalized = new(ReferenceEqualityComparer.Instance);
        Stack<(GraphPattern Node, bool Rebuild)> work = new();
        work.Push((root, Rebuild: false));

        while(work.Count > 0)
        {
            (GraphPattern node, bool rebuild) = work.Pop();
            if(rebuild)
            {
                normalized[node] = RebuildPattern(node, normalized);

                continue;
            }

            List<GraphPattern> children = PatternChildren(node);
            if(children.Count == 0)
            {
                //A basic block (rebuilt with lowered triples) or a passthrough member (BIND/FILTER/VALUES/error).
                normalized[node] = RebuildPattern(node, normalized);
            }
            else
            {
                work.Push((node, Rebuild: true));
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    work.Push((children[i], Rebuild: false));
                }
            }
        }

        return normalized[root];
    }

    /// <summary>Returns the nested patterns a pattern must have normalized before it is rebuilt.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The nested patterns, in source order; empty for a basic block or a passthrough member.</returns>
    private static List<GraphPattern> PatternChildren(GraphPattern pattern)
    {
        switch(pattern)
        {
            case GroupGraphPattern group:
            {
                return [.. group.Members];
            }

            case OptionalPattern optional:
            {
                return [optional.Inner];
            }

            case MinusPattern minus:
            {
                return [minus.Inner];
            }

            case UnionPattern union:
            {
                return [union.Left, union.Right];
            }

            case GraphGraphPattern graph:
            {
                return [graph.Inner];
            }

            case ServicePattern service:
            {
                return [service.Inner];
            }

            case SubSelectPattern subSelect:
            {
                //A sub-SELECT is a SELECT (no CONSTRUCT template), so normalizing it is normalizing its WHERE pattern.
                return [subSelect.InnerQuery.Where.Pattern];
            }

            default:
            {
                //BasicGraphPatternBlock (rebuilt as a leaf) and BIND / FILTER / VALUES / error patterns (which
                //carry no triple-pattern sugar) have no nested pattern to normalize.
                return [];
            }
        }
    }

    /// <summary>Rebuilds a pattern from its already-normalized nested patterns (looked up by reference); a basic block is lowered and a passthrough member returned unchanged.</summary>
    /// <param name="node">The pattern to rebuild.</param>
    /// <param name="normalized">The map of already-normalized nested patterns.</param>
    /// <returns>The normalized pattern.</returns>
    private GraphPattern RebuildPattern(GraphPattern node, Dictionary<GraphPattern, GraphPattern> normalized)
    {
        switch(node)
        {
            case GroupGraphPattern group:
            {
                List<GraphPattern> members = new(group.Members.Count);
                foreach(GraphPattern member in group.Members)
                {
                    members.Add(normalized[member]);
                }

                return new GroupGraphPattern(group.Span, members);
            }

            case BasicGraphPatternBlock block:
            {
                return NormalizeBlock(block);
            }

            case OptionalPattern optional:
            {
                return new OptionalPattern(optional.Span, normalized[optional.Inner]);
            }

            case MinusPattern minus:
            {
                return new MinusPattern(minus.Span, normalized[minus.Inner]);
            }

            case UnionPattern union:
            {
                return new UnionPattern(union.Span, normalized[union.Left], normalized[union.Right]);
            }

            case GraphGraphPattern graph:
            {
                return new GraphGraphPattern(graph.Span, graph.GraphTerm, normalized[graph.Inner]);
            }

            case ServicePattern service:
            {
                return new ServicePattern(service.Span, service.Endpoint, service.IsSilent, normalized[service.Inner]);
            }

            case SubSelectPattern subSelect:
            {
                SparqlQuery inner = subSelect.InnerQuery;
                SparqlQuery normalizedInner = inner with { Where = inner.Where with { Pattern = normalized[inner.Where.Pattern] } };

                return new SubSelectPattern(subSelect.Span, normalizedInner);
            }

            default:
            {
                //BIND / FILTER expressions, VALUES data, and error-node patterns carry no triple-pattern
                //sugar to lower (a triple term in an expression or VALUES is already a core term), so they
                //flow through unchanged.
                return node;
            }
        }
    }

    /// <summary>Rebuilds a basic graph pattern block so its triples hold only core terms, folding the block's standalone reified triples into the same run.</summary>
    /// <param name="block">The block to normalize.</param>
    /// <returns>A block whose <see cref="BasicGraphPatternBlock.Triples"/> are fully lowered and whose <see cref="BasicGraphPatternBlock.StandaloneNodes"/> are empty.</returns>
    private BasicGraphPatternBlock NormalizeBlock(BasicGraphPatternBlock block)
    {
        List<TriplePattern> result = [];
        foreach(TriplePattern triple in block.Triples)
        {
            LowerTriple(triple, result);
        }

        //A standalone reified triple has no enclosing predicate; lowering it asserts only its reification
        //(and, under the opt-in flag, its base triple). The reifier it yields is discarded.
        foreach(TriplePatternTerm standalone in block.StandaloneNodes)
        {
            _ = LowerTerm(standalone, result);
        }

        return new BasicGraphPatternBlock(block.Span, result, []);
    }

    /// <summary>Lowers one source triple — its subject, predicate, and object (including any object annotations) — into <paramref name="sink"/>.</summary>
    /// <param name="triple">The source triple pattern.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    private void LowerTriple(TriplePattern triple, List<TriplePattern> sink)
    {
        TriplePatternTerm subject = LowerTerm(triple.Subject, sink);
        EmitObject(subject, triple.Predicate, triple.Object, triple.Span, sink);
    }

    /// <summary>
    /// Emits a single <c>subject predicate object</c> triple, lowering the object term and — when the
    /// object carries RDF 1.2 annotations — the reification triples those annotations describe.
    /// </summary>
    /// <param name="subject">The already-lowered subject term.</param>
    /// <param name="predicate">The predicate term (an IRI, variable, or property path), passed through unchanged.</param>
    /// <param name="objectTerm">The object term, possibly an <see cref="AnnotatedObject"/> or sugar.</param>
    /// <param name="span">The source span attributed to the emitted base triple.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    private void EmitObject(TriplePatternTerm subject, TriplePatternTerm predicate, TriplePatternTerm objectTerm, SourceSpan span, List<TriplePattern> sink)
    {
        if(objectTerm is AnnotatedObject annotated)
        {
            //The annotation syntax both reifies and asserts: the base triple is emitted, then the
            //annotations are lowered as reification triples about it.
            TriplePatternTerm annotatedValue = LowerTerm(annotated.Object, sink);
            sink.Add(new TriplePattern(span, subject, predicate, annotatedValue));
            LowerAnnotations(annotated.Annotations, subject, predicate, annotatedValue, sink);

            return;
        }

        TriplePatternTerm value = LowerTerm(objectTerm, sink);
        sink.Add(new TriplePattern(span, subject, predicate, value));
    }

    /// <summary>
    /// Lowers a term in subject, object, or reifier position to a core term, appending any triples its
    /// sugar expands into. Core terms, property paths, and error placeholders pass through unchanged.
    /// </summary>
    /// <param name="term">The term to lower.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <returns>The core term to use in the enclosing position.</returns>
    private TriplePatternTerm LowerTerm(TriplePatternTerm term, List<TriplePattern> sink)
    {
        return term switch
        {
            ConstantTerm or VariableTerm or PropertyPathTerm or ErrorTriplePatternTerm => term,
            AstTripleTerm tripleTerm => LowerTripleTerm(tripleTerm, sink),
            CollectionTerm collection => LowerCollection(collection, sink),
            BlankNodePropertyListTerm propertyList => LowerBlankNodePropertyList(propertyList, sink),
            ReifiedTriple reified => LowerReifiedTriple(reified, sink),

            //An AnnotatedObject only occurs in object position, where EmitObject intercepts it; any other
            //term kind here is an internal invariant violation, not recoverable user input.
            _ => throw new InvalidOperationException($"Unexpected term kind {term.GetType().Name} in a non-object position during SPARQL normalization.")
        };
    }

    /// <summary>Lowers a quoted triple term <c>&lt;&lt;( s p o )&gt;&gt;</c>: a core term whose inner positions are themselves lowered. A triple term asserts nothing.</summary>
    /// <param name="tripleTerm">The quoted triple term.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <returns>The quoted triple term with its inner positions lowered.</returns>
    private AstTripleTerm LowerTripleTerm(AstTripleTerm tripleTerm, List<TriplePattern> sink)
    {
        //A triple term's inner subject/object are grammar-restricted to non-asserting terms (IRI, variable,
        //literal, or a nested triple term), so lowering them never appends to the sink; doing so keeps the
        //recursion uniform without asserting the quoted triple.
        TriplePattern inner = tripleTerm.Inner;
        TriplePattern lowered = new(
            inner.Span,
            LowerTerm(inner.Subject, sink),
            inner.Predicate,
            LowerTerm(inner.Object, sink));

        return new AstTripleTerm(tripleTerm.Span, lowered);
    }

    /// <summary>Lowers an RDF collection to an <c>rdf:first</c>/<c>rdf:rest</c>/<c>rdf:nil</c> chain; an empty collection is <c>rdf:nil</c>.</summary>
    /// <param name="collection">The collection term.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <returns>The head of the chain (a fresh blank node), or <c>rdf:nil</c> for an empty collection.</returns>
    private TriplePatternTerm LowerCollection(CollectionTerm collection, List<TriplePattern> sink)
    {
        if(collection.Items.Count == 0)
        {
            return new ConstantTerm(collection.Span, new NamedNode(RdfVocabulary.Rdf.Nil));
        }

        ConstantTerm first = new(collection.Span, new NamedNode(RdfVocabulary.Rdf.First));
        ConstantTerm rest = new(collection.Span, new NamedNode(RdfVocabulary.Rdf.Rest));
        ConstantTerm nil = new(collection.Span, new NamedNode(RdfVocabulary.Rdf.Nil));

        TriplePatternTerm head = FreshBlankNode(collection.Span);
        TriplePatternTerm current = head;
        for(int i = 0; i < collection.Items.Count; i++)
        {
            TriplePatternTerm item = LowerTerm(collection.Items[i], sink);
            sink.Add(new TriplePattern(collection.Span, current, first, item));

            if(i == collection.Items.Count - 1)
            {
                sink.Add(new TriplePattern(collection.Span, current, rest, nil));
            }
            else
            {
                TriplePatternTerm next = FreshBlankNode(collection.Span);
                sink.Add(new TriplePattern(collection.Span, current, rest, next));
                current = next;
            }
        }

        return head;
    }

    /// <summary>Lowers a blank-node property list to a fresh blank node carrying its predicate-object triples.</summary>
    /// <param name="propertyList">The blank-node property list term.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <returns>The fresh blank node standing for the property list.</returns>
    private TriplePatternTerm LowerBlankNodePropertyList(BlankNodePropertyListTerm propertyList, List<TriplePattern> sink)
    {
        TriplePatternTerm head = FreshBlankNode(propertyList.Span);
        LowerProperties(head, propertyList.Properties, sink);

        return head;
    }

    /// <summary>Lowers a reified triple to its <c>rdf:reifies</c> reification triple (and, under the opt-in flag, its base triple).</summary>
    /// <param name="reified">The reified triple term.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <returns>The reifier (the explicit identity, or a fresh blank node) — the reified triple's value.</returns>
    private TriplePatternTerm LowerReifiedTriple(ReifiedTriple reified, List<TriplePattern> sink)
    {
        TriplePattern inner = reified.Inner;
        TriplePattern loweredInner = new(
            inner.Span,
            LowerTerm(inner.Subject, sink),
            inner.Predicate,
            LowerTerm(inner.Object, sink));

        TriplePatternTerm reifier = reified.Reifier is null
            ? FreshBlankNode(reified.Span)
            : LowerTerm(reified.Reifier, sink);

        ConstantTerm reifies = new(reified.Span, new NamedNode(Vocabulary.Rdf.Reifies));
        AstTripleTerm tripleTerm = new(reified.Span, loweredInner);
        sink.Add(new TriplePattern(reified.Span, reifier, reifies, tripleTerm));

        //RDF 1.2 (Turtle §2.11 / §7.3.2): a reified triple does not assert its inner triple. The opt-in
        //flag additionally asserts it — a deliberate non-standard extension.
        if(options.AssertReifiedTripleInnerTriple)
        {
            sink.Add(loweredInner);
        }

        return reifier;
    }

    /// <summary>
    /// Lowers the RDF 1.2 annotations on a stated triple into reification triples. A reifier (<c>~ id?</c>)
    /// emits <c>reifier rdf:reifies &lt;&lt;( s p o )&gt;&gt;</c> and stays pending for an immediately
    /// following block; an annotation block (<c>{| … |}</c>) reuses the pending reifier or allocates a
    /// fresh one, then attaches its predicate-object list to that reifier. Blocks nest naturally: an
    /// annotated object inside a block annotates that block's own annotation triple.
    /// </summary>
    /// <param name="annotations">The annotations attached to the object, in source order.</param>
    /// <param name="subject">The stated triple's subject.</param>
    /// <param name="predicate">The stated triple's predicate.</param>
    /// <param name="objectTerm">The stated triple's object.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    private void LowerAnnotations(IReadOnlyList<Annotation> annotations, TriplePatternTerm subject, TriplePatternTerm predicate, TriplePatternTerm objectTerm, List<TriplePattern> sink)
    {
        TriplePatternTerm? pendingReifier = null;

        foreach(Annotation annotation in annotations)
        {
            switch(annotation)
            {
                case ReifierAnnotation reifierAnnotation:
                {
                    TriplePatternTerm reifier = reifierAnnotation.Reifier is null
                        ? FreshBlankNode(reifierAnnotation.Span)
                        : LowerTerm(reifierAnnotation.Reifier, sink);

                    sink.Add(ReificationTriple(reifier, subject, predicate, objectTerm, reifierAnnotation.Span));
                    pendingReifier = reifier;

                    break;
                }

                case AnnotationBlock block:
                {
                    TriplePatternTerm blockReifier;
                    if(pendingReifier is not null)
                    {
                        blockReifier = pendingReifier;
                    }
                    else
                    {
                        blockReifier = FreshBlankNode(block.Span);
                        sink.Add(ReificationTriple(blockReifier, subject, predicate, objectTerm, block.Span));
                    }

                    LowerProperties(blockReifier, block.Properties, sink);
                    pendingReifier = null;

                    break;
                }

                default:
                {
                    //An ErrorAnnotation carries its own diagnostic; there is nothing to lower.
                    break;
                }
            }
        }
    }

    /// <summary>Lowers a predicate-object list against a subject, reusing the object path so nested annotations and sugar lower the same way.</summary>
    /// <param name="subject">The subject the properties attach to.</param>
    /// <param name="properties">The predicate-object entries.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    private void LowerProperties(TriplePatternTerm subject, IReadOnlyList<PropertyListPath> properties, List<TriplePattern> sink)
    {
        foreach(PropertyListPath property in properties)
        {
            foreach(TriplePatternTerm objectTerm in property.Objects)
            {
                EmitObject(subject, property.Verb, objectTerm, property.Span, sink);
            }
        }
    }

    /// <summary>Builds a <c>reifier rdf:reifies &lt;&lt;( subject predicate object )&gt;&gt;</c> reification triple.</summary>
    /// <param name="reifier">The reifier term.</param>
    /// <param name="subject">The reified triple's subject.</param>
    /// <param name="predicate">The reified triple's predicate.</param>
    /// <param name="objectTerm">The reified triple's object.</param>
    /// <param name="span">The source span attributed to the triple.</param>
    /// <returns>The reification triple pattern.</returns>
    private static TriplePattern ReificationTriple(TriplePatternTerm reifier, TriplePatternTerm subject, TriplePatternTerm predicate, TriplePatternTerm objectTerm, SourceSpan span)
    {
        ConstantTerm reifies = new(span, new NamedNode(Vocabulary.Rdf.Reifies));
        AstTripleTerm tripleTerm = new(span, new TriplePattern(span, subject, predicate, objectTerm));

        return new TriplePattern(span, reifier, reifies, tripleTerm);
    }

    /// <summary>Allocates a fresh blank-node term through the configured delegate.</summary>
    /// <param name="span">The source span attributed to the blank node.</param>
    /// <returns>A <see cref="ConstantTerm"/> wrapping the freshly-labelled blank node.</returns>
    private ConstantTerm FreshBlankNode(SourceSpan span)
    {
        BlankNodeRequest request = new(Guid.Empty, ReadOnlyMemory<byte>.Empty, span, pool);
        Utf8String label = blankNodes(in request);

        return new ConstantTerm(span, new BlankNode(label));
    }
}
