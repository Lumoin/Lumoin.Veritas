using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

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
        switch(term)
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
                CollectNamespaces(tripleTerm.Subject, sink);
                sink.Add(NamespaceOf(tripleTerm.Predicate.Iri.ToString()));
                CollectNamespaces(tripleTerm.Object, sink);
                break;
            }
            default:
            {
                break;
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
            case(TripleTerm tripleTerm):
            {
                output.WriteUtf8Literal("<<( "u8);
                WriteTermInline(output, tripleTerm.Subject, prefixes);
                output.WriteByte((byte)' ');
                WriteNamedNode(output, tripleTerm.Predicate, prefixes);
                output.WriteByte((byte)' ');
                WriteTermInline(output, tripleTerm.Object, prefixes);
                output.WriteUtf8Literal(" )>>"u8);
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
        return term switch
        {
            NamedNode named => "i:" + named.Iri.ToString(),
            BlankNode blank => "b:" + blank.Label.ToString(),
            Literal lit => "l:" + lit.Value.ToString() + ":" + (lit.Language?.ToString() ?? string.Empty) + ":" + lit.Datatype.Iri.ToString(),
            TripleTerm tt => "t:" + TermKey(tt.Subject) + "|" + tt.Predicate.Iri.ToString() + "|" + TermKey(tt.Object),
            _ => "x:" + term.ToString()
        };
    }
}
