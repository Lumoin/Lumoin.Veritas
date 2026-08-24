using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Parsing;

namespace Lumoin.Veritas.Turtle;

/// <summary>
/// Serialises a <see cref="Quad"/> stream into RDF 1.2 Turtle or TriG
/// at Level 1 fidelity: semantically equivalent output without
/// preserving the original document's whitespace, comments, or
/// punctuation style.
/// </summary>
/// <remarks>
/// <para>
/// The writer groups quads by named graph (TriG only) and within each
/// graph by subject, emits prefix declarations for any reference it
/// can shorten, and collapses repeated subject-predicate pairs into
/// <c>;</c> / <c>,</c> punctuation. Collections, blank-node property
/// lists, and other surface-syntax sugar are not reconstructed from
/// quads; the writer always emits explicit triples.
/// </para>
/// <para>
/// Level 2 fidelity — preserving comments, original formatting, and
/// surface-syntax sugar — is deferred. It requires an AST-aware
/// writer that consumes <see cref="Ast.TurtleDocument"/> directly
/// rather than a quad stream, and that needs trivia attachment
/// points on the AST nodes that the current parser does not produce.
/// </para>
/// </remarks>
public static class TurtleWriter
{
    private static readonly Dictionary<string, string> CommonPrefixes = new(StringComparer.Ordinal)
    {
        { "http://www.w3.org/1999/02/22-rdf-syntax-ns#", "rdf" },
        { "http://www.w3.org/2000/01/rdf-schema#", "rdfs" },
        { "http://www.w3.org/2001/XMLSchema#", "xsd" },
        { "http://www.w3.org/2002/07/owl#", "owl" },
        { "http://www.w3.org/ns/shacl#", "sh" },
        { "http://www.w3.org/2004/02/skos/core#", "skos" }
    };

    /// <summary>
    /// Serialises a <see cref="Quad"/> stream as Turtle or TriG and
    /// writes the bytes to <paramref name="output"/>.
    /// </summary>
    /// <param name="quads">The quads to serialise.</param>
    /// <param name="output">The pipe to write UTF-8 bytes to.</param>
    /// <param name="syntax">Whether to emit Turtle or TriG.</param>
    /// <param name="options">Optional writer knobs.</param>
    /// <param name="cancellationToken">A token to cancel writing.</param>
    /// <returns>A task that completes when all bytes have been written.</returns>
    public static async Task WriteAsync(
        IAsyncEnumerable<Quad> quads,
        PipeWriter output,
        TurtleSyntax syntax,
        TurtleWriterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(output);

        List<Quad> materialised = [];
        await foreach(Quad quad in quads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            materialised.Add(quad);
        }

        WriteCore(materialised, output, syntax, options ?? new TurtleWriterOptions());

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        await output.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Serialises an in-memory list of quads as Turtle or TriG.
    /// </summary>
    /// <param name="quads">The quads to serialise.</param>
    /// <param name="output">The pipe to write UTF-8 bytes to.</param>
    /// <param name="syntax">Whether to emit Turtle or TriG.</param>
    /// <param name="options">Optional writer knobs.</param>
    public static void Write(
        IReadOnlyList<Quad> quads,
        PipeWriter output,
        TurtleSyntax syntax,
        TurtleWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(output);

        WriteCore(quads, output, syntax, options ?? new TurtleWriterOptions());
        output.Complete();
    }

    private static void WriteCore(
        IReadOnlyList<Quad> quads,
        PipeWriter output,
        TurtleSyntax syntax,
        TurtleWriterOptions options)
    {
        Dictionary<string, string> prefixes = BuildPrefixes(quads, options);

        //The output is already an IBufferWriter<byte>; UTF-8 bytes are written straight into the
        //pipe's buffer. The caller-facing method owns the pipe's flush/complete.
        if(options.BaseIri is { } baseIri)
        {
            output.WriteUtf8Literal("@base <"u8);
            output.WriteUtf8String(baseIri);
            output.WriteUtf8Literal("> .\n"u8);
        }

        foreach(KeyValuePair<string, string> kv in prefixes)
        {
            output.WriteUtf8Literal("@prefix "u8);
            output.WriteUtf8(kv.Value);
            output.WriteUtf8Literal(": <"u8);
            output.WriteUtf8(kv.Key);
            output.WriteUtf8Literal("> .\n"u8);
        }

        if(prefixes.Count > 0 || options.BaseIri is not null)
        {
            output.WriteByte((byte)'\n');
        }

        if(syntax == TurtleSyntax.TriG)
        {
            WriteTriG(output, quads, prefixes, options);
        }
        else
        {
            WriteTurtle(output, quads, prefixes, options);
        }
    }

    private static Dictionary<string, string> BuildPrefixes(IReadOnlyList<Quad> quads, TurtleWriterOptions options)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);

        if(options.Prefixes is not null)
        {
            foreach(KeyValuePair<Utf8String, Utf8String> entry in options.Prefixes)
            {
                result[entry.Value.ToString()] = entry.Key.ToString();
            }
        }

        if(!options.AutoDeclareCommonPrefixes)
        {
            return result;
        }

        HashSet<string> referencedNamespaces = new(StringComparer.Ordinal);
        for(int i = 0; i < quads.Count; i++)
        {
            CollectNamespaces(quads[i].Subject, referencedNamespaces);
            referencedNamespaces.Add(NamespaceOf(quads[i].Predicate.Iri.ToString()));
            CollectNamespaces(quads[i].Object, referencedNamespaces);
            if(quads[i].Graph is not null)
            {
                CollectNamespaces(quads[i].Graph, referencedNamespaces);
            }
        }

        foreach(KeyValuePair<string, string> entry in CommonPrefixes)
        {
            if(referencedNamespaces.Contains(entry.Key) && !result.ContainsKey(entry.Key))
            {
                result[entry.Key] = entry.Value;
            }
        }

        return result;
    }

    private static void CollectNamespaces(RdfTerm? term, HashSet<string> sink)
    {
        //Walk over an explicit stack (no recursion); a quoted triple's per-frame depth bounds nesting with a
        //catchable exception. Push order is irrelevant — the sink is an unordered set.
        Stack<NamespaceWalkStep> work = new();
        work.Push(new NamespaceWalkStep(term, 0));

        while(work.Count > 0)
        {
            NamespaceWalkStep step = work.Pop();
            switch(step.Term)
            {
                case(NamedNode named):
                {
                    sink.Add(NamespaceOf(named.Iri.ToString()));
                    break;
                }
                case(Literal literal):
                {
                    sink.Add(NamespaceOf(literal.Datatype.Iri.ToString()));
                    break;
                }
                case(TripleTerm tripleTerm):
                {
                    int next = step.Depth + 1;
                    if(next > QuotedTripleLimits.MaxNestingDepth)
                    {
                        throw new TripleTermDepthLimitException(next, QuotedTripleLimits.MaxNestingDepth);
                    }

                    sink.Add(NamespaceOf(tripleTerm.Predicate.Iri.ToString()));
                    work.Push(new NamespaceWalkStep(tripleTerm.Subject, next));
                    work.Push(new NamespaceWalkStep(tripleTerm.Object, next));
                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    private static string NamespaceOf(string iri)
    {
        //Split the IRI at the last '#' or '/' for prefix expansion.
        int hash = iri.LastIndexOf('#');
        int slash = iri.LastIndexOf('/');
        int splitAt = Math.Max(hash, slash);
        return splitAt < 0 ? iri : iri[..(splitAt + 1)];
    }

    private static void WriteTurtle(
        IBufferWriter<byte> output,
        IReadOnlyList<Quad> quads,
        Dictionary<string, string> prefixes,
        TurtleWriterOptions options)
    {
        WriteGroup(output, quads, prefixes, options, indent: "");
    }

    private static void WriteTriG(
        IBufferWriter<byte> output,
        IReadOnlyList<Quad> quads,
        Dictionary<string, string> prefixes,
        TurtleWriterOptions options)
    {
        //Bucket quads by their graph term (using string identity on the graph IRI / blank-node label).
        Dictionary<string, List<Quad>> defaultBucket = new(StringComparer.Ordinal);
        Dictionary<string, (RdfTerm Graph, List<Quad> Quads)> namedBuckets = new(StringComparer.Ordinal);

        for(int i = 0; i < quads.Count; i++)
        {
            Quad quad = quads[i];
            if(quad.Graph is null)
            {
                if(!defaultBucket.TryGetValue(string.Empty, out List<Quad>? list))
                {
                    list = [];
                    defaultBucket[string.Empty] = list;
                }

                list.Add(quad);
                continue;
            }

            string key = GraphKey(quad.Graph);
            if(!namedBuckets.TryGetValue(key, out (RdfTerm Graph, List<Quad> Quads) bucket))
            {
                bucket = (quad.Graph, []);
                namedBuckets[key] = bucket;
            }

            bucket.Quads.Add(quad);
        }

        if(defaultBucket.TryGetValue(string.Empty, out List<Quad>? defaultList) && defaultList.Count > 0)
        {
            WriteGroup(output, defaultList, prefixes, options, indent: "");
            output.WriteByte((byte)'\n');
        }

        foreach(KeyValuePair<string, (RdfTerm Graph, List<Quad> Quads)> entry in namedBuckets)
        {
            WriteTermInline(output, entry.Value.Graph, prefixes);
            output.WriteUtf8Literal(" {\n"u8);
            WriteGroup(output, entry.Value.Quads, prefixes, options, indent: options.Indent);
            output.WriteUtf8Literal("}\n\n"u8);
        }
    }

    private static string GraphKey(RdfTerm graph)
    {
        return graph switch
        {
            NamedNode named => "i:" + named.Iri.ToString(),
            BlankNode blank => "b:" + blank.Label.ToString(),
            _ => "x:" + graph.ToString()
        };
    }

    private static void WriteGroup(
        IBufferWriter<byte> output,
        IReadOnlyList<Quad> quads,
        Dictionary<string, string> prefixes,
        TurtleWriterOptions options,
        string indent)
    {
        //Group quads by subject so we can emit subject once with ; / , punctuation.
        Dictionary<string, (RdfTerm Subject, Dictionary<string, (NamedNode Predicate, List<RdfTerm> Objects)> Predicates)> bySubject =
            new(StringComparer.Ordinal);
        List<string> subjectOrder = [];

        for(int i = 0; i < quads.Count; i++)
        {
            Quad quad = quads[i];
            string subjectKey = TermKey(quad.Subject);
            if(!bySubject.TryGetValue(subjectKey, out (RdfTerm Subject, Dictionary<string, (NamedNode Predicate, List<RdfTerm> Objects)> Predicates) subjectEntry))
            {
                subjectEntry = (quad.Subject, new Dictionary<string, (NamedNode Predicate, List<RdfTerm> Objects)>(StringComparer.Ordinal));
                bySubject[subjectKey] = subjectEntry;
                subjectOrder.Add(subjectKey);
            }

            string predicateKey = TermKey(quad.Predicate);
            if(!subjectEntry.Predicates.TryGetValue(predicateKey, out (NamedNode Predicate, List<RdfTerm> Objects) predicateEntry))
            {
                predicateEntry = (quad.Predicate, []);
                subjectEntry.Predicates[predicateKey] = predicateEntry;
            }

            predicateEntry.Objects.Add(quad.Object);
        }

        foreach(string subjectKey in subjectOrder)
        {
            (RdfTerm Subject, Dictionary<string, (NamedNode Predicate, List<RdfTerm> Objects)> Predicates) subjectEntry = bySubject[subjectKey];
            output.WriteUtf8(indent);
            WriteTermInline(output, subjectEntry.Subject, prefixes);

            bool firstPredicate = true;
            foreach(KeyValuePair<string, (NamedNode Predicate, List<RdfTerm> Objects)> predicateEntry in subjectEntry.Predicates)
            {
                if(firstPredicate)
                {
                    output.WriteByte((byte)' ');
                    firstPredicate = false;
                }
                else
                {
                    output.WriteUtf8Literal(" ;\n"u8);
                    output.WriteUtf8(indent);
                    output.WriteUtf8(options.Indent);
                }

                WriteTermInline(output, predicateEntry.Value.Predicate, prefixes);
                output.WriteByte((byte)' ');

                for(int oi = 0; oi < predicateEntry.Value.Objects.Count; oi++)
                {
                    if(oi > 0)
                    {
                        output.WriteUtf8Literal(" , "u8);
                    }

                    WriteTermInline(output, predicateEntry.Value.Objects[oi], prefixes);
                }
            }

            output.WriteUtf8Literal(" .\n"u8);
        }
    }

    private static void WriteTermInline(
        IBufferWriter<byte> output,
        RdfTerm term,
        Dictionary<string, string> prefixes)
    {
        //A quoted triple is the only nesting term; everything else is a leaf written directly. Walking the
        //triple over an explicit stack keeps deep nesting off the call stack; the depth guard turns a
        //pathological term into a catchable exception. The predicate stays prefix-aware via WriteNamedNode.
        if(term is not TripleTerm root)
        {
            WriteLeafInline(output, term, prefixes);

            return;
        }

        Stack<TermStep> work = new();
        work.Push(new TermStep(StepKind.Term, root, null));
        int depth = 0;

        while(work.Count > 0)
        {
            TermStep step = work.Pop();
            switch(step.Kind)
            {
                case(StepKind.Term):
                {
                    if(step.Term is TripleTerm triple)
                    {
                        depth++;
                        if(depth > QuotedTripleLimits.MaxNestingDepth)
                        {
                            throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                        }

                        //Push the components in reverse so they pop in serialization order:
                        //<<( subject SPACE predicate SPACE object )>>.
                        work.Push(TermStep.Close);
                        work.Push(new TermStep(StepKind.Term, triple.Object, null));
                        work.Push(TermStep.Space);
                        work.Push(new TermStep(StepKind.Predicate, null, triple.Predicate));
                        work.Push(TermStep.Space);
                        work.Push(new TermStep(StepKind.Term, triple.Subject, null));
                        work.Push(TermStep.Open);
                    }
                    else
                    {
                        WriteLeafInline(output, step.Term!, prefixes);
                    }

                    break;
                }
                case(StepKind.Open):
                {
                    output.WriteUtf8Literal(TripleTermOpen);
                    break;
                }
                case(StepKind.Space):
                {
                    output.WriteByte((byte)' ');
                    break;
                }
                case(StepKind.Predicate):
                {
                    WriteNamedNode(output, step.Predicate!, prefixes);
                    break;
                }
                default:
                {
                    //StepKind.Close: the triple's components are written; close it and unwind one nesting level.
                    output.WriteUtf8Literal(TripleTermClose);
                    depth--;
                    break;
                }
            }
        }
    }

    private static void WriteLeafInline(
        IBufferWriter<byte> output,
        RdfTerm term,
        Dictionary<string, string> prefixes)
    {
        switch(term)
        {
            case(NamedNode named):
            {
                WriteNamedNode(output, named, prefixes);
                break;
            }
            case(BlankNode blank):
            {
                output.WriteUtf8Literal("_:"u8);
                output.WriteUtf8String(blank.Label);
                break;
            }
            case(Literal literal):
            {
                WriteLiteral(output, literal, prefixes);
                break;
            }
            case(EngineNode engine):
            {
                //An engine-minted node serializes as its deterministic Skolem IRI; the rendering re-parses as
                //an ordinary named node, never back into an engine mint.
                output.WriteByte((byte)'<');
                output.WriteUtf8String(engine.SkolemIri());
                output.WriteByte((byte)'>');
                break;
            }
            default:
            {
                output.WriteUtf8(term.ToString());
                break;
            }
        }
    }

    private static void WriteNamedNode(
        IBufferWriter<byte> output,
        NamedNode named,
        Dictionary<string, string> prefixes)
    {
        string iri = named.Iri.ToString();
        string ns = NamespaceOf(iri);
        if(prefixes.TryGetValue(ns, out string? prefix))
        {
            string local = iri[ns.Length..];
            if(IsSimpleLocal(local))
            {
                output.WriteUtf8(prefix);
                output.WriteByte((byte)':');
                output.WriteUtf8(local);
                return;
            }
        }

        output.WriteByte((byte)'<');
        output.WriteUtf8String(named.Iri);
        output.WriteByte((byte)'>');
    }

    private static bool IsSimpleLocal(string local)
    {
        //Conservative: only emit prefixed form when the local name contains
        //characters that need no escaping per PN_LOCAL. Letters, digits,
        //and underscore are safe.
        if(local.Length == 0)
        {
            return false;
        }

        for(int i = 0; i < local.Length; i++)
        {
            char c = local[i];
            bool ok = char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-';
            if(!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteLiteral(IBufferWriter<byte> output, Literal literal, Dictionary<string, string> prefixes)
    {
        output.WriteByte((byte)'"');
        AppendEscapedString(output, literal.Value.Span);
        output.WriteByte((byte)'"');

        if(literal.Language is { } lang)
        {
            output.WriteByte((byte)'@');
            output.WriteUtf8String(lang);
            if(literal.BaseDirection is { } direction)
            {
                output.WriteUtf8Literal("--"u8);
                output.WriteUtf8Literal(direction switch
                {
                    TextDirection.Ltr => "ltr"u8,
                    TextDirection.Rtl => "rtl"u8,
                    _ => "ltr"u8
                });
            }

            return;
        }

        if(literal.Datatype.Iri.Span.SequenceEqual("http://www.w3.org/2001/XMLSchema#string"u8))
        {
            return;
        }

        output.WriteUtf8Literal("^^"u8);
        WriteNamedNode(output, literal.Datatype, prefixes);
    }

    private static void AppendEscapedString(IBufferWriter<byte> output, ReadOnlySpan<byte> value)
    {
        //Scan the UTF-8 bytes, copying maximal runs that need no escaping and emitting the
        //two-byte escape for each of the five Turtle string-escape bytes. The escaped bytes are
        //all ASCII, so they never occur inside a multi-byte UTF-8 sequence and runs stay intact.
        int runStart = 0;
        for(int i = 0; i < value.Length; i++)
        {
            ReadOnlySpan<byte> escape = value[i] switch
            {
                (byte)'\\' => "\\\\"u8,
                (byte)'"' => "\\\""u8,
                (byte)'\n' => "\\n"u8,
                (byte)'\r' => "\\r"u8,
                (byte)'\t' => "\\t"u8,
                _ => default
            };

            if(escape.IsEmpty)
            {
                continue;
            }

            output.WriteUtf8Literal(value[runStart..i]);
            output.WriteUtf8Literal(escape);
            runStart = i + 1;
        }

        output.WriteUtf8Literal(value[runStart..]);
    }

    private static string TermKey(RdfTerm term)
    {
        //A non-triple term keys directly. A quoted triple is folded post-order over an explicit stack so a
        //deeply-nested term cannot overflow: each level is expanded (children scheduled) then combined once its
        //children's keys are memoised. The memo is keyed by reference only — distinct value-equal instances
        //miss the cache and recompute the identical key, so grouping is preserved; it never returns a wrong key.
        if(term is not TripleTerm root)
        {
            return LeafKey(term);
        }

        Dictionary<RdfTerm, string> done = new(ReferenceEqualityComparer.Instance);
        Stack<KeyStep> work = new();
        work.Push(new KeyStep(root, false, 0));

        while(work.Count > 0)
        {
            KeyStep step = work.Pop();
            if(step.Term is not TripleTerm triple)
            {
                done[step.Term] = LeafKey(step.Term);

                continue;
            }

            if(!step.Expanded)
            {
                int next = step.Depth + 1;
                if(next > QuotedTripleLimits.MaxNestingDepth)
                {
                    throw new TripleTermDepthLimitException(next, QuotedTripleLimits.MaxNestingDepth);
                }

                work.Push(new KeyStep(triple, true, step.Depth));
                work.Push(new KeyStep(triple.Subject, false, next));
                work.Push(new KeyStep(triple.Object, false, next));
            }
            else
            {
                //The predicate stays inlined (its raw IRI), not routed through LeafKey, to keep the key bytes.
                done[triple] = "t:" + done[triple.Subject] + "|" + triple.Predicate.Iri.ToString() + "|" + done[triple.Object];
            }
        }

        return done[root];
    }

    private static string LeafKey(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => "i:" + named.Iri.ToString(),
            BlankNode blank => "b:" + blank.Label.ToString(),
            Literal lit => "l:" + lit.Value.ToString() + ":" + (lit.Language?.ToString() ?? string.Empty) + ":" + lit.Datatype.Iri.ToString(),
            _ => "x:" + term.ToString()
        };
    }

    /// <summary>One step on the namespace-collection work-stack; depth bounds quoted-triple nesting.</summary>
    /// <param name="Term">The term whose namespaces to collect, or <see langword="null"/>.</param>
    /// <param name="Depth">The quoted-triple nesting depth at which this term was scheduled.</param>
    private readonly record struct NamespaceWalkStep(RdfTerm? Term, int Depth);

    /// <summary>One step on the grouping-key work-stack: a term to key, in either its expand or its combine phase.</summary>
    /// <param name="Term">The term to key.</param>
    /// <param name="Expanded">Whether the term's components have been scheduled (the combine phase).</param>
    /// <param name="Depth">The quoted-triple nesting depth at which this term was scheduled.</param>
    private readonly record struct KeyStep(RdfTerm Term, bool Expanded, int Depth);

    /// <summary>The RDF-star quoted-triple opening delimiter <c>&lt;&lt;( </c>.</summary>
    private static ReadOnlySpan<byte> TripleTermOpen => "<<( "u8;

    /// <summary>The RDF-star quoted-triple closing delimiter <c> )&gt;&gt;</c>.</summary>
    private static ReadOnlySpan<byte> TripleTermClose => " )>>"u8;

    /// <summary>The kind of step on the inline-term serialization work-stack.</summary>
    private enum StepKind
    {
        /// <summary>Visit a term: write a leaf directly, or expand a quoted triple into its component steps.</summary>
        Term,

        /// <summary>Write the quoted-triple opening delimiter.</summary>
        Open,

        /// <summary>Write a single component separator.</summary>
        Space,

        /// <summary>Write a quoted triple's predicate, which is always a named node.</summary>
        Predicate,

        /// <summary>Write the quoted-triple closing delimiter and unwind one nesting level.</summary>
        Close
    }

    /// <summary>One step on the inline-term serialization work-stack.</summary>
    /// <param name="Kind">The step kind.</param>
    /// <param name="Term">The term to visit; set only for a <c>Term</c> step.</param>
    /// <param name="Predicate">The predicate to write; set only for a <c>Predicate</c> step.</param>
    private readonly record struct TermStep(StepKind Kind, RdfTerm? Term, NamedNode? Predicate)
    {
        /// <summary>The payload-less step that writes the quoted-triple opening delimiter.</summary>
        public static TermStep Open { get; } = new(StepKind.Open, null, null);

        /// <summary>The payload-less step that writes a single component separator.</summary>
        public static TermStep Space { get; } = new(StepKind.Space, null, null);

        /// <summary>The payload-less step that writes the quoted-triple closing delimiter and unwinds one nesting level.</summary>
        public static TermStep Close { get; } = new(StepKind.Close, null, null);
    }
}
