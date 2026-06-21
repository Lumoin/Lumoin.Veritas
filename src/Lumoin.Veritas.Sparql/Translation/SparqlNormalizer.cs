using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
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
    /// <returns>The request with sugar lowered to plain triple patterns (both query forms and update operations).</returns>
    public SparqlRequest Normalize(SparqlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request switch
        {
            SparqlQuery query => NormalizeQuery(query),
            SparqlUpdateRequest update => NormalizeUpdate(update),
            _ => request
        };
    }

    /// <summary>Normalizes every operation of an update request, lowering the RDF 1.2 sugar in its quad blocks and modify <c>WHERE</c> patterns.</summary>
    /// <param name="update">The update request.</param>
    /// <returns>The normalized update request.</returns>
    private SparqlUpdateRequest NormalizeUpdate(SparqlUpdateRequest update)
    {
        List<UpdateOperation> operations = new(update.Operations.Count);
        foreach(UpdateOperation operation in update.Operations)
        {
            operations.Add(NormalizeOperation(operation));
        }

        return update with { Operations = operations };
    }

    /// <summary>Normalizes one update operation: its quad blocks (templates / data) and any modify <c>WHERE</c> pattern; graph-management operations pass through unchanged.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The normalized operation.</returns>
    private UpdateOperation NormalizeOperation(UpdateOperation operation)
        => operation switch
        {
            InsertDataOperation insert => insert with { Data = NormalizeQuads(insert.Data) },
            DeleteDataOperation delete => delete with { Data = NormalizeQuads(delete.Data) },
            DeleteWhereOperation deleteWhere => deleteWhere with { Pattern = NormalizeQuads(deleteWhere.Pattern) },
            ModifyOperation modify => modify with
            {
                Delete = modify.Delete is null ? null : NormalizeQuads(modify.Delete),
                Insert = modify.Insert is null ? null : NormalizeQuads(modify.Insert),
                Where = (GroupGraphPattern)NormalizePattern(modify.Where)
            },
            _ => operation
        };

    /// <summary>Lowers a quad block's default-graph triples and each <c>GRAPH</c> group's triples to plain triple patterns.</summary>
    /// <param name="quads">The quad block.</param>
    /// <returns>The lowered quad block.</returns>
    private Quads NormalizeQuads(Quads quads)
    {
        List<QuadsGraphGroup> groups = new(quads.GraphGroups.Count);
        foreach(QuadsGraphGroup group in quads.GraphGroups)
        {
            groups.Add(group with { Triples = NormalizeTemplate(group.Triples, group.StandaloneNodes), StandaloneNodes = [] });
        }

        return quads with
        {
            DefaultTriples = NormalizeTemplate(quads.DefaultTriples, quads.DefaultStandaloneNodes),
            GraphGroups = groups,
            DefaultStandaloneNodes = []
        };
    }

    /// <summary>Normalizes a query: its CONSTRUCT template (if any) and its WHERE pattern.</summary>
    /// <param name="query">The query to normalize.</param>
    /// <returns>The normalized query.</returns>
    private SparqlQuery NormalizeQuery(SparqlQuery query)
    {
        QueryForm form = query.Form is ConstructQuery construct
            ? new ConstructQuery(construct.Span, NormalizeTemplate(construct.Template, construct.TemplateStandaloneNodes), [])
            : query.Form;

        WhereClause where = query.Where with { Pattern = NormalizePattern(query.Where.Pattern) };

        return query with { Form = form, Where = where };
    }

    /// <summary>Lowers the triples and standalone nodes of a CONSTRUCT template or quad block to plain triple patterns.</summary>
    /// <param name="template">The template triples.</param>
    /// <param name="standaloneNodes">The standalone <c>TriplesNode</c> subjects (no enclosing predicate) whose own triples are lowered into the result.</param>
    /// <returns>The flattened, lowered template triples.</returns>
    private List<TriplePattern> NormalizeTemplate(IReadOnlyList<TriplePattern> template, IReadOnlyList<TriplePatternTerm> standaloneNodes)
    {
        List<TriplePattern> result = [];
        LowerTriples(template, standaloneNodes, result);

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
        LowerTriples(block.Triples, block.StandaloneNodes, result);

        return new BasicGraphPatternBlock(block.Span, result, []);
    }

    /// <summary>
    /// Lowers a run of source triples (and a block's standalone reified-triple assertions) into
    /// <paramref name="sink"/> over an explicit work-list (no recursion): each triple's object is queued as a
    /// task, and a collection / blank-node property list / annotation block expands by queueing further tasks
    /// rather than recursing, so arbitrarily deep RDF 1.2 sugar cannot overflow the stack.
    /// </summary>
    /// <param name="triples">The source triples, each <c>subject predicate object</c> (the object possibly sugar or annotated).</param>
    /// <param name="standaloneNodes">The block's standalone reified triples (subject-only assertions); empty for a CONSTRUCT template.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    private void LowerTriples(IReadOnlyList<TriplePattern> triples, IReadOnlyList<TriplePatternTerm> standaloneNodes, List<TriplePattern> sink)
    {
        Queue<EmitObjectTask> work = new();

        foreach(TriplePattern triple in triples)
        {
            //The subject is resolved to a core term (its own sugar emitted / queued); the object is then queued.
            TriplePatternTerm subject = ResolveCore(triple.Subject, sink, work);
            work.Enqueue(new EmitObjectTask(subject, triple.Predicate, triple.Object, triple.Span));
        }

        //A standalone reified triple has no enclosing predicate; resolving it asserts only its reification
        //(and, under the opt-in flag, its base triple). The reifier it yields is discarded.
        foreach(TriplePatternTerm standalone in standaloneNodes)
        {
            _ = ResolveCore(standalone, sink, work);
        }

        while(work.Count > 0)
        {
            EmitObject(work.Dequeue(), sink, work);
        }
    }

    /// <summary>
    /// Emits one <c>subject predicate object</c> triple, resolving the object to a core term and — when the
    /// object carries RDF 1.2 annotations — the reification triples those annotations describe. Sugar in the
    /// object expands by queueing further tasks onto <paramref name="work"/>, never by recursing.
    /// </summary>
    /// <param name="task">The pending emission: subject, predicate, object (possibly an <see cref="AnnotatedObject"/> or sugar), and span.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <param name="work">The work-list onto which the object's expansion queues further emissions.</param>
    private void EmitObject(EmitObjectTask task, List<TriplePattern> sink, Queue<EmitObjectTask> work)
    {
        if(task.Object is AnnotatedObject annotated)
        {
            //The annotation syntax both reifies and asserts: the base triple is emitted, then the annotations
            //are lowered as reification triples about it.
            TriplePatternTerm annotatedValue = ResolveCore(annotated.Object, sink, work);
            sink.Add(new TriplePattern(task.Span, task.Subject, task.Predicate, annotatedValue));
            EmitAnnotations(annotated.Annotations, task.Subject, task.Predicate, annotatedValue, sink, work);

            return;
        }

        TriplePatternTerm value = ResolveCore(task.Object, sink, work);
        sink.Add(new TriplePattern(task.Span, task.Subject, task.Predicate, value));
    }

    /// <summary>
    /// Lowers the RDF 1.2 annotations on a stated triple into reification triples. A reifier (<c>~ id?</c>)
    /// emits <c>reifier rdf:reifies &lt;&lt;( s p o )&gt;&gt;</c> and stays pending for an immediately following
    /// block; an annotation block (<c>{| … |}</c>) reuses the pending reifier or allocates a fresh one, then
    /// queues its predicate-object list against that reifier. Blocks nest naturally — an annotated object inside
    /// a block annotates that block's own annotation triple — via the same work-list, without recursing.
    /// </summary>
    /// <param name="annotations">The annotations attached to the object, in source order.</param>
    /// <param name="subject">The stated triple's subject.</param>
    /// <param name="predicate">The stated triple's predicate.</param>
    /// <param name="objectTerm">The stated triple's object.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <param name="work">The work-list onto which an annotation block's properties are queued.</param>
    private void EmitAnnotations(IReadOnlyList<Annotation> annotations, TriplePatternTerm subject, TriplePatternTerm predicate, TriplePatternTerm objectTerm, List<TriplePattern> sink, Queue<EmitObjectTask> work)
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
                        : ResolveCore(reifierAnnotation.Reifier, sink, work);

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

                    //The block's properties annotate the block's reifier; queue them so nested annotations and
                    //sugar lower iteratively through the same work-list.
                    foreach(PropertyListPath property in block.Properties)
                    {
                        foreach(TriplePatternTerm propertyObject in property.Objects)
                        {
                            work.Enqueue(new EmitObjectTask(blockReifier, property.Verb, propertyObject, property.Span));
                        }
                    }

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

    /// <summary>
    /// Resolves a term in subject, object, item, or reifier position to its core term, emitting the triples its
    /// sugar expands into and queueing any deferred expansion onto <paramref name="work"/>. Core terms, property
    /// paths, and error placeholders pass through. The one bottom-up case — the cores embedded inside a quoted or
    /// reified triple term — is resolved with a local post-order stack so nested triple terms cannot overflow.
    /// </summary>
    /// <param name="root">The term to resolve.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <param name="work">The work-list onto which a collection's items / a property list's objects are queued.</param>
    /// <returns>The core term to use in the enclosing position.</returns>
    private TriplePatternTerm ResolveCore(TriplePatternTerm root, List<TriplePattern> sink, Queue<EmitObjectTask> work)
    {
        //Only quoted/reified triple terms embed their inner cores and so need bottom-up resolution; every other
        //term's core is allocated immediately (its contents deferred), so the common path skips the stack.
        if(root is not (AstTripleTerm or ReifiedTriple))
        {
            return ResolveImmediateCore(root, sink, work);
        }

        Dictionary<TriplePatternTerm, TriplePatternTerm> cores = new(ReferenceEqualityComparer.Instance);
        Stack<(TriplePatternTerm Term, bool Build, int Depth)> pending = new();
        pending.Push((root, Build: false, Depth: 1));

        while(pending.Count > 0)
        {
            (TriplePatternTerm term, bool build, int depth) = pending.Pop();
            if(build)
            {
                cores[term] = BuildQuotedCore(term, cores, sink);

                continue;
            }

            switch(term)
            {
                case AstTripleTerm tripleTerm:
                {
                    if(depth > QuotedTripleLimits.MaxNestingDepth)
                    {
                        throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                    }

                    pending.Push((tripleTerm, Build: true, depth));
                    pending.Push((tripleTerm.Inner.Object, Build: false, depth + 1));
                    pending.Push((tripleTerm.Inner.Subject, Build: false, depth + 1));

                    break;
                }

                case ReifiedTriple reified:
                {
                    if(depth > QuotedTripleLimits.MaxNestingDepth)
                    {
                        throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                    }

                    pending.Push((reified, Build: true, depth));
                    if(reified.Reifier is not null)
                    {
                        pending.Push((reified.Reifier, Build: false, depth + 1));
                    }

                    pending.Push((reified.Inner.Object, Build: false, depth + 1));
                    pending.Push((reified.Inner.Subject, Build: false, depth + 1));

                    break;
                }

                default:
                {
                    cores[term] = ResolveImmediateCore(term, sink, work);

                    break;
                }
            }
        }

        return cores[root];
    }

    /// <summary>
    /// Resolves a term whose core does not embed any child core: a core term (passed through), an empty
    /// collection (<c>rdf:nil</c>), a non-empty collection (a fresh head, its <c>rdf:rest</c> backbone emitted
    /// and its items queued), or a blank-node property list (a fresh node, its properties queued).
    /// </summary>
    /// <param name="term">The term to resolve; never a quoted or reified triple term (those take the post-order path).</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <param name="work">The work-list onto which a collection's items / a property list's objects are queued.</param>
    /// <returns>The core term to use in the enclosing position.</returns>
    private TriplePatternTerm ResolveImmediateCore(TriplePatternTerm term, List<TriplePattern> sink, Queue<EmitObjectTask> work)
    {
        switch(term)
        {
            case ConstantTerm or VariableTerm or PropertyPathTerm or ErrorTriplePatternTerm:
            {
                return term;
            }

            case CollectionTerm collection:
            {
                return ResolveCollection(collection, sink, work);
            }

            case BlankNodePropertyListTerm propertyList:
            {
                TriplePatternTerm head = FreshBlankNode(propertyList.Span);
                foreach(PropertyListPath property in propertyList.Properties)
                {
                    foreach(TriplePatternTerm propertyObject in property.Objects)
                    {
                        work.Enqueue(new EmitObjectTask(head, property.Verb, propertyObject, property.Span));
                    }
                }

                return head;
            }

            default:
            {
                //An AnnotatedObject only occurs in object position, where EmitObject intercepts it; any other
                //term kind here is an internal invariant violation, not recoverable user input.
                throw new InvalidOperationException($"Unexpected term kind {term.GetType().Name} in a non-object position during SPARQL normalization.");
            }
        }
    }

    /// <summary>Resolves a non-empty collection to its fresh head, emitting the <c>rdf:rest</c> backbone (terminated by <c>rdf:nil</c>) and queueing each item as an <c>rdf:first</c> emission; an empty collection is <c>rdf:nil</c>.</summary>
    /// <param name="collection">The collection term.</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <param name="work">The work-list onto which the items are queued.</param>
    /// <returns>The head of the chain (a fresh blank node), or <c>rdf:nil</c> for an empty collection.</returns>
    private TriplePatternTerm ResolveCollection(CollectionTerm collection, List<TriplePattern> sink, Queue<EmitObjectTask> work)
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
            //Each item is lowered as the object of an rdf:first triple, queued so nested sugar expands iteratively.
            work.Enqueue(new EmitObjectTask(current, first, collection.Items[i], collection.Span));

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

    /// <summary>Builds the core of a quoted or reified triple term from its already-resolved inner cores; a reified triple also emits its <c>rdf:reifies</c> triple (and, under the opt-in flag, its base triple).</summary>
    /// <param name="term">The quoted (<see cref="AstTripleTerm"/>) or reified (<see cref="ReifiedTriple"/>) triple term.</param>
    /// <param name="cores">The resolved cores of the inner positions (and the explicit reifier, if any).</param>
    /// <param name="sink">The list lowered triples are appended to.</param>
    /// <returns>The quoted triple term (for <see cref="AstTripleTerm"/>) or the reifier (for <see cref="ReifiedTriple"/>).</returns>
    private TriplePatternTerm BuildQuotedCore(TriplePatternTerm term, Dictionary<TriplePatternTerm, TriplePatternTerm> cores, List<TriplePattern> sink)
    {
        switch(term)
        {
            case AstTripleTerm tripleTerm:
            {
                TriplePattern inner = tripleTerm.Inner;
                TriplePattern lowered = new(inner.Span, cores[inner.Subject], inner.Predicate, cores[inner.Object]);

                return new AstTripleTerm(tripleTerm.Span, lowered);
            }

            case ReifiedTriple reified:
            {
                TriplePattern inner = reified.Inner;
                TriplePattern loweredInner = new(inner.Span, cores[inner.Subject], inner.Predicate, cores[inner.Object]);

                TriplePatternTerm reifier = reified.Reifier is null
                    ? FreshBlankNode(reified.Span)
                    : cores[reified.Reifier];

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

            default:
            {
                throw new InvalidOperationException($"Unexpected term kind {term.GetType().Name} in BuildQuotedCore during SPARQL normalization.");
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

    /// <summary>
    /// One pending object emission on the term-lowering work-list: a <c>subject predicate object</c> triple to
    /// emit, where the object may still be RDF 1.2 sugar or an <see cref="AnnotatedObject"/>.
    /// </summary>
    /// <param name="Subject">The (already core) subject term.</param>
    /// <param name="Predicate">The predicate term, passed through unchanged.</param>
    /// <param name="Object">The object term to lower and emit.</param>
    /// <param name="Span">The source span attributed to the emitted base triple.</param>
    private readonly record struct EmitObjectTask(TriplePatternTerm Subject, TriplePatternTerm Predicate, TriplePatternTerm Object, SourceSpan Span);
}
