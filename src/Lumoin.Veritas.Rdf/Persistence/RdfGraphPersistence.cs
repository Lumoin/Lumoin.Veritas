using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Streaming N-Triples writer for an RDF graph expressed as a
/// <see cref="LabeledGraphSource{TNode, TLabel}"/> over
/// <see cref="TermId"/> nodes and <see cref="IriId"/> predicate
/// labels.
/// </summary>
/// <remarks>
/// <para>
/// This is the bridge from "abstract labeled graph in
/// <c>Core.Algebra</c>" to "RDF concrete syntax on disk". The writer
/// streams the graph's edge enumerator once and resolves each
/// <see cref="TermId"/> through the supplied
/// <see cref="TermDictionary"/> as it goes. No materialisation, no
/// dictionary copy.
/// </para>
/// <para>
/// <b>Surface.</b> The canonical entry point takes a
/// <see cref="PipeWriter"/>; a <see cref="Stream"/> convenience
/// overload wraps the stream with <c>leaveOpen: true</c> and
/// forwards.
/// </para>
/// <para>
/// <b>Format.</b> One triple per line, terminated by <c>" .\n"</c>,
/// per the W3C N-Triples 1.2 grammar:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="NamedNode"/> → <c>&lt;iri&gt;</c></description></item>
///   <item><description><see cref="BlankNode"/> → <c>_:label</c></description></item>
///   <item><description><see cref="Literal"/> → <c>"value"^^&lt;datatype&gt;</c> or <c>"value"@lang</c></description></item>
/// </list>
/// <para>
/// Escape rules cover the canonical N-Triples set: backslash,
/// double-quote, newline, carriage return, tab. Sufficient for the
/// synthetic and round-trip-test corpora this writer is intended for.
/// </para>
/// <para>
/// <b>Encoding.</b> UTF-8 text is written through the shared
/// <see cref="Utf8BufferWriter"/> extensions over the pipe; byte-literal
/// delimiters are copied verbatim. Both live in
/// <c>Lumoin.Veritas.Core.Encoding</c>, imported above alongside
/// <see cref="Utf8String"/>.
/// </para>
/// </remarks>
public static class RdfGraphPersistence
{
    private static ReadOnlySpan<byte> Space => " "u8;
    private static ReadOnlySpan<byte> EndTriple => " .\n"u8;
    private static ReadOnlySpan<byte> AngleOpen => "<"u8;
    private static ReadOnlySpan<byte> AngleClose => ">"u8;
    private static ReadOnlySpan<byte> BlankPrefix => "_:"u8;
    private static ReadOnlySpan<byte> Quote => "\""u8;
    private static ReadOnlySpan<byte> AtSign => "@"u8;
    private static ReadOnlySpan<byte> DatatypePrefix => "^^<"u8;

    /// <summary>
    /// Writes <paramref name="source"/> as N-Triples to
    /// <paramref name="writer"/>.
    /// </summary>
    public static async Task WriteNTriplesAsync(
        LabeledGraphSource<TermId, IriId> source,
        TermDictionary dictionary,
        PipeWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(writer);

        await foreach((TermId src, IriId pred, TermId tgt) in source.Edges(cancellationToken).ConfigureAwait(false))
        {
            WriteTerm(writer, dictionary.Resolve(src));
            writer.WriteUtf8Literal(Space);
            WriteTerm(writer, dictionary.Resolve(pred.Value));
            writer.WriteUtf8Literal(Space);
            WriteTerm(writer, dictionary.Resolve(tgt));
            writer.WriteUtf8Literal(EndTriple);
            FlushResult flush = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if(flush.IsCompleted || flush.IsCanceled)
            {
                break;
            }
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience overload: wraps <paramref name="stream"/> in a
    /// <see cref="PipeWriter"/> with <c>leaveOpen: true</c> and
    /// forwards.
    /// </summary>
    public static Task WriteNTriplesAsync(
        LabeledGraphSource<TermId, IriId> source,
        TermDictionary dictionary,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PipeWriter writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));

        return WriteNTriplesAsync(source, dictionary, writer, cancellationToken);
    }

    //Term-to-N-Triples-form. Dispatches on RdfTerm subtype. The string
    //materialisation per term is the one allocation that remains;
    //eliminating it requires a byte-span accessor on Utf8String which
    //can replace the Utf8String.ToString() calls drop-in.
    private static void WriteTerm(PipeWriter writer, RdfTerm term)
    {
        switch(term)
        {
            case NamedNode named:
            {
                writer.WriteUtf8Literal(AngleOpen);
                writer.WriteUtf8(named.Iri.ToString());
                writer.WriteUtf8Literal(AngleClose);

                break;
            }
            case BlankNode blank:
            {
                writer.WriteUtf8Literal(BlankPrefix);
                writer.WriteUtf8(blank.Label.ToString());

                break;
            }
            case Literal literal:
            {
                writer.WriteUtf8Literal(Quote);
                WriteEscapedLiteralValue(writer, literal.Value.ToString());
                writer.WriteUtf8Literal(Quote);
                if(literal.Language is { } lang)
                {
                    writer.WriteUtf8Literal(AtSign);
                    writer.WriteUtf8(lang.ToString());
                }
                else
                {
                    writer.WriteUtf8Literal(DatatypePrefix);
                    writer.WriteUtf8(literal.Datatype.Iri.ToString());
                    writer.WriteUtf8Literal(AngleClose);
                }

                break;
            }
            default:
            {
                throw new NotSupportedException(
                    $"N-Triples serialisation of {term.GetType().Name} is not supported by this writer.");
            }
        }
    }

    //Walks the input once. Clean runs are emitted in bulk via
    //GetBytes(span, span); each metacharacter is written as a
    //pre-encoded UTF-8 byte literal. This minimises the number of
    //GetSpan/Advance round trips compared to per-character writes.
    private static void WriteEscapedLiteralValue(PipeWriter writer, string value)
    {
        if(value.Length == 0)
        {
            return;
        }

        int runStart = 0;
        for(int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            ReadOnlySpan<byte> escape = c switch
            {
                '\\' => "\\\\"u8,
                '"' => "\\\""u8,
                '\n' => "\\n"u8,
                '\r' => "\\r"u8,
                '\t' => "\\t"u8,
                _ => default,
            };

            if(escape.IsEmpty)
            {
                continue;
            }

            //Flush the clean run before the escape character.
            if(i > runStart)
            {
                writer.WriteUtf8(value.AsSpan(runStart, i - runStart));
            }
            writer.WriteUtf8Literal(escape);
            runStart = i + 1;
        }

        //Trailing clean run.
        if(runStart < value.Length)
        {
            writer.WriteUtf8(value.AsSpan(runStart));
        }
    }

}
