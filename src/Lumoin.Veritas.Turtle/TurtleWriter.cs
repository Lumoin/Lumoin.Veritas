using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;

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

        //The serialisation is text-based; drive the existing StreamWriter logic over a Stream view
        //of the pipe. leaveOpen keeps pipe completion the caller-facing method's responsibility.
        using Stream stream = output.AsStream(leaveOpen: true);
        using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 8192, leaveOpen: true);
        writer.NewLine = "\n";

        if(options.BaseIri is { } baseIri)
        {
            writer.Write("@base <");
            writer.Write(baseIri.ToString());
            writer.Write("> .\n");
        }

        foreach(KeyValuePair<string, string> kv in prefixes)
        {
            writer.Write("@prefix ");
            writer.Write(kv.Value);
            writer.Write(": <");
            writer.Write(kv.Key);
            writer.Write("> .\n");
        }

        if(prefixes.Count > 0 || options.BaseIri is not null)
        {
            writer.Write("\n");
        }

        if(syntax == TurtleSyntax.TriG)
        {
            WriteTriG(writer, quads, prefixes, options);
        }
        else
        {
            WriteTurtle(writer, quads, prefixes, options);
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
            case NamedNode named:
            {
                sink.Add(NamespaceOf(named.Iri.ToString()));
                break;
            }

            case Literal literal:
            {
                sink.Add(NamespaceOf(literal.Datatype.Iri.ToString()));
                break;
            }

            case TripleTerm tripleTerm:
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
        StreamWriter writer,
        IReadOnlyList<Quad> quads,
        Dictionary<string, string> prefixes,
        TurtleWriterOptions options)
    {
        WriteGroup(writer, quads, prefixes, options, indent: "");
    }

    private static void WriteTriG(
        StreamWriter writer,
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
            WriteGroup(writer, defaultList, prefixes, options, indent: "");
            writer.Write("\n");
        }

        foreach(KeyValuePair<string, (RdfTerm Graph, List<Quad> Quads)> entry in namedBuckets)
        {
            WriteTermInline(writer, entry.Value.Graph, prefixes);
            writer.Write(" {\n");
            WriteGroup(writer, entry.Value.Quads, prefixes, options, indent: options.Indent);
            writer.Write("}\n\n");
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
        StreamWriter writer,
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
            writer.Write(indent);
            WriteTermInline(writer, subjectEntry.Subject, prefixes);

            bool firstPredicate = true;
            foreach(KeyValuePair<string, (NamedNode Predicate, List<RdfTerm> Objects)> predicateEntry in subjectEntry.Predicates)
            {
                if(firstPredicate)
                {
                    writer.Write(" ");
                    firstPredicate = false;
                }
                else
                {
                    writer.Write(" ;\n");
                    writer.Write(indent);
                    writer.Write(options.Indent);
                }

                WriteTermInline(writer, predicateEntry.Value.Predicate, prefixes);
                writer.Write(" ");

                for(int oi = 0; oi < predicateEntry.Value.Objects.Count; oi++)
                {
                    if(oi > 0)
                    {
                        writer.Write(" , ");
                    }

                    WriteTermInline(writer, predicateEntry.Value.Objects[oi], prefixes);
                }
            }

            writer.Write(" .\n");
        }
    }

    private static void WriteTermInline(
        StreamWriter writer,
        RdfTerm term,
        Dictionary<string, string> prefixes)
    {
        switch(term)
        {
            case NamedNode named:
            {
                WriteNamedNode(writer, named, prefixes);
                break;
            }

            case BlankNode blank:
            {
                writer.Write("_:");
                writer.Write(blank.Label.ToString());
                break;
            }

            case Literal literal:
            {
                WriteLiteral(writer, literal, prefixes);
                break;
            }

            case TripleTerm tripleTerm:
            {
                writer.Write("<<( ");
                WriteTermInline(writer, tripleTerm.Subject, prefixes);
                writer.Write(" ");
                WriteNamedNode(writer, tripleTerm.Predicate, prefixes);
                writer.Write(" ");
                WriteTermInline(writer, tripleTerm.Object, prefixes);
                writer.Write(" )>>");
                break;
            }

            default:
            {
                writer.Write(term.ToString());
                break;
            }
        }
    }

    private static void WriteNamedNode(
        StreamWriter writer,
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
                writer.Write(prefix);
                writer.Write(':');
                writer.Write(local);
                return;
            }
        }

        writer.Write('<');
        writer.Write(iri);
        writer.Write('>');
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

    private static void WriteLiteral(StreamWriter writer, Literal literal, Dictionary<string, string> prefixes)
    {
        writer.Write('"');
        AppendEscapedString(writer, literal.Value.ToString());
        writer.Write('"');

        if(literal.Language is { } lang)
        {
            writer.Write('@');
            writer.Write(lang.ToString());
            if(literal.BaseDirection is { } direction)
            {
                writer.Write("--");
                writer.Write(direction switch
                {
                    TextDirection.Ltr => "ltr",
                    TextDirection.Rtl => "rtl",
                    _ => "ltr"
                });
            }

            return;
        }

        string datatypeIri = literal.Datatype.Iri.ToString();
        if(string.Equals(datatypeIri, "http://www.w3.org/2001/XMLSchema#string", StringComparison.Ordinal))
        {
            return;
        }

        writer.Write("^^");
        WriteNamedNode(writer, literal.Datatype, prefixes);
    }

    private static void AppendEscapedString(StreamWriter writer, string value)
    {
        for(int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch(c)
            {
                case '\\':
                {
                    writer.Write("\\\\");
                    break;
                }

                case '"':
                {
                    writer.Write("\\\"");
                    break;
                }

                case '\n':
                {
                    writer.Write("\\n");
                    break;
                }

                case '\r':
                {
                    writer.Write("\\r");
                    break;
                }

                case '\t':
                {
                    writer.Write("\\t");
                    break;
                }

                default:
                {
                    writer.Write(c);
                    break;
                }
            }
        }
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
