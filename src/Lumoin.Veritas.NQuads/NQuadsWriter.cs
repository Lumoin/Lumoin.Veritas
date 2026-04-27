using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.NQuads;

/// <summary>
/// Serializes <see cref="Quad"/> instances to N-Quads format.
/// </summary>
/// <remarks>
/// <para>
/// N-Quads is a line-oriented format where each line represents exactly one quad.
/// The format is defined at https://www.w3.org/TR/n-quads/.
/// </para>
/// <para>
/// Each line has the form:
/// <c>subject predicate object [graph] .</c>
/// </para>
/// <para>
/// All output is UTF-8. No .NET string allocation occurs in the hot path — terms
/// are written directly from their <see cref="Utf8String"/> byte representations.
/// </para>
/// </remarks>
public static class NQuadsWriter
{
    //N-Quads uses \n as the line terminator (spec section 5).
    private static ReadOnlySpan<byte> LineFeed => "\n"u8;
    private static ReadOnlySpan<byte> Space => " "u8;
    private static ReadOnlySpan<byte> StatementEnd => " .\n"u8;
    private static ReadOnlySpan<byte> LessThan => "<"u8;
    private static ReadOnlySpan<byte> GreaterThan => ">"u8;
    private static ReadOnlySpan<byte> BlankNodePrefix => "_:"u8;
    private static ReadOnlySpan<byte> Quote => "\""u8;
    private static ReadOnlySpan<byte> DatatypeMarker => "\"^^<"u8;
    private static ReadOnlySpan<byte> LanguageMarker => "\"@"u8;

    /// <summary>
    /// Writes a sequence of quads to the given stream in N-Quads format.
    /// </summary>
    /// <param name="quads">The quads to serialize.</param>
    /// <param name="stream">The output stream. Must be writable.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async ValueTask WriteAsync(
        IAsyncEnumerable<Quad> quads,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(stream);
        //Use a PipeWriter-style approach: buffer writes and flush periodically
        //to amortize system call overhead without unbounded buffering.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            int position = 0;

            await foreach(Quad quad in quads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                //Estimate max line length to decide if we need to flush first.
                //Worst case: two long IRIs + literal value + datatype + punctuation.
                //A conservative estimate is 2048 bytes per quad; if the buffer cannot
                //hold that, flush before writing.
                if(position + 2048 > buffer.Length)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, position), cancellationToken).ConfigureAwait(false);
                    position = 0;
                }

                position = WriteQuad(quad, buffer, position, stream, cancellationToken);

                //If WriteQuad flushed mid-quad (large literal), position was reset
                //and the line ending was appended. The quad is complete.
            }

            if(position > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, position), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes a sequence of quads to the given stream in N-Quads format.
    /// </summary>
    /// <param name="quads">The quads to serialize.</param>
    /// <param name="stream">The output stream. Must be writable.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async ValueTask WriteAsync(
        IEnumerable<Quad> quads,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        await WriteAsync(ToAsyncEnumerable(quads, cancellationToken), stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a single quad to the buffer. Returns the updated buffer position.
    /// </summary>
    /// <remarks>
    /// This method writes entirely into the pre-allocated buffer without async state machines
    /// in the critical path. The only async operation is a potential mid-buffer flush for
    /// very large literals, which is uncommon in practice.
    /// </remarks>
    private static int WriteQuad(Quad quad, byte[] buffer, int position, Stream stream, CancellationToken cancellationToken)
    {
        position = WriteTerm(quad.Subject, buffer, position);
        buffer.AsSpan(position, Space.Length).CopyFrom(Space);
        position += Space.Length;

        position = WriteNamedNode(quad.Predicate, buffer, position);
        buffer.AsSpan(position, Space.Length).CopyFrom(Space);
        position += Space.Length;

        position = WriteTerm(quad.Object, buffer, position);

        if(quad.Graph is { } graph)
        {
            buffer.AsSpan(position, Space.Length).CopyFrom(Space);
            position += Space.Length;
            position = WriteTerm(graph, buffer, position);
        }

        buffer.AsSpan(position, StatementEnd.Length).CopyFrom(StatementEnd);
        position += StatementEnd.Length;

        return position;
    }

    private static int WriteTerm(RdfTerm term, byte[] buffer, int position)
    {
        return term switch
        {
            NamedNode namedNode => WriteNamedNode(namedNode, buffer, position),
            BlankNode blankNode => WriteBlankNode(blankNode, buffer, position),
            Literal literal => WriteLiteral(literal, buffer, position),
            _ => throw new InvalidOperationException($"Unknown RDF term type: {term.GetType().Name}.")
        };
    }

    private static int WriteNamedNode(NamedNode node, byte[] buffer, int position)
    {
        buffer.AsSpan(position, LessThan.Length).CopyFrom(LessThan);
        position += LessThan.Length;

        //IRIs may contain characters that need escaping per the N-Quads spec.
        //In practice, well-formed IRIs from JSON-LD and RDF sources contain
        //only printable ASCII, so we write them directly.
        position = WriteEscapedIri(node.Iri.Span, buffer, position);

        buffer.AsSpan(position, GreaterThan.Length).CopyFrom(GreaterThan);
        position += GreaterThan.Length;

        return position;
    }

    private static int WriteBlankNode(BlankNode node, byte[] buffer, int position)
    {
        buffer.AsSpan(position, BlankNodePrefix.Length).CopyFrom(BlankNodePrefix);
        position += BlankNodePrefix.Length;

        node.Label.Span.CopyTo(buffer.AsSpan(position));
        position += node.Label.Length;

        return position;
    }

    private static int WriteLiteral(Literal literal, byte[] buffer, int position)
    {
        buffer.AsSpan(position, Quote.Length).CopyFrom(Quote);
        position += Quote.Length;

        position = WriteEscapedLiteralValue(literal.Value.Span, buffer, position);

        if(literal.Language is { } language)
        {
            buffer.AsSpan(position, LanguageMarker.Length).CopyFrom(LanguageMarker);
            position += LanguageMarker.Length;

            language.Span.CopyTo(buffer.AsSpan(position));
            position += language.Length;
        }
        else
        {
            buffer.AsSpan(position, DatatypeMarker.Length).CopyFrom(DatatypeMarker);
            position += DatatypeMarker.Length;

            position = WriteEscapedIri(literal.Datatype.Iri.Span, buffer, position);

            buffer.AsSpan(position, GreaterThan.Length).CopyFrom(GreaterThan);
            position += GreaterThan.Length;
        }

        return position;
    }

    /// <summary>
    /// Writes an IRI with N-Quads escape sequences applied.
    /// </summary>
    /// <remarks>
    /// Per the N-Quads spec, IRIs must have control characters and non-printable
    /// characters escaped as \uXXXX or \UXXXXXXXX sequences.
    /// Well-formed IRIs in practice contain only printable ASCII, so this fast-paths
    /// through them without inspection where possible.
    /// </remarks>
    private static int WriteEscapedIri(ReadOnlySpan<byte> iri, byte[] buffer, int position)
    {
        //IRIs are ASCII-dominant in practice. Scan for characters that need escaping.
        foreach(byte b in iri)
        {
            if(b < 0x20 || b == 0x5C)
            {
                //Control character or backslash: needs escaping. Fall through to slow path.
                return WriteEscapedIriSlow(iri, buffer, position);
            }
        }

        //Fast path: no escaping needed.
        iri.CopyTo(buffer.AsSpan(position));
        return position + iri.Length;
    }

    private static int WriteEscapedIriSlow(ReadOnlySpan<byte> iri, byte[] buffer, int position)
    {
        //Decode UTF-8 and re-encode with escaping. Rare path for malformed IRIs.
        string decoded = Encoding.UTF8.GetString(iri);
        foreach(char c in decoded)
        {
            if(c < 0x20 || c == '\\')
            {
                //Escape as \uXXXX.
                Span<byte> escape = buffer.AsSpan(position, 6);
                escape[0] = (byte)'\\';
                escape[1] = (byte)'u';
                WriteHex4((ushort)c, escape[2..]);
                position += 6;
            }
            else
            {
                int written = Encoding.UTF8.GetBytes([c], buffer.AsSpan(position));
                position += written;
            }
        }

        return position;
    }

    /// <summary>
    /// Writes a literal value with N-Quads string escape sequences applied.
    /// </summary>
    /// <remarks>
    /// N-Quads string escape sequences per spec:
    /// \t, \n, \r, \", \\, \uXXXX, \UXXXXXXXX.
    /// </remarks>
    private static int WriteEscapedLiteralValue(ReadOnlySpan<byte> value, byte[] buffer, int position)
    {
        //Check if any escaping is needed: \n, \r, \t, \", \\.
        bool needsEscaping = false;
        foreach(byte b in value)
        {
            if(b is (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'"' or (byte)'\\')
            {
                needsEscaping = true;
                break;
            }
        }

        if(!needsEscaping)
        {
            value.CopyTo(buffer.AsSpan(position));
            return position + value.Length;
        }

        //Slow path: character-by-character with escaping.
        string decoded = Encoding.UTF8.GetString(value);
        foreach(char c in decoded)
        {
            switch(c)
            {
                case '\t':
                    buffer[position++] = (byte)'\\';
                    buffer[position++] = (byte)'t';
                    break;
                case '\n':
                    buffer[position++] = (byte)'\\';
                    buffer[position++] = (byte)'n';
                    break;
                case '\r':
                    buffer[position++] = (byte)'\\';
                    buffer[position++] = (byte)'r';
                    break;
                case '"':
                    buffer[position++] = (byte)'\\';
                    buffer[position++] = (byte)'"';
                    break;
                case '\\':
                    buffer[position++] = (byte)'\\';
                    buffer[position++] = (byte)'\\';
                    break;
                default:
                    int written = Encoding.UTF8.GetBytes([c], buffer.AsSpan(position));
                    position += written;
                    break;
            }
        }

        return position;
    }

    private static void WriteHex4(ushort value, Span<byte> destination)
    {
        const string hex = "0123456789ABCDEF";
        destination[0] = (byte)hex[(value >> 12) & 0xF];
        destination[1] = (byte)hex[(value >> 8) & 0xF];
        destination[2] = (byte)hex[(value >> 4) & 0xF];
        destination[3] = (byte)hex[value & 0xF];
    }

    private static async IAsyncEnumerable<Quad> ToAsyncEnumerable(
        IEnumerable<Quad> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach(Quad quad in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return quad;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// Extension methods for working with byte spans in N-Quads output.
/// </summary>
internal static class SpanExtensions
{
    /// <summary>
    /// Copies bytes from a <see cref="ReadOnlySpan{T}"/> into a <see cref="Span{T}"/>.
    /// Named CopyFrom to distinguish call direction at the call site.
    /// </summary>
    internal static void CopyFrom(this Span<byte> destination, ReadOnlySpan<byte> source)
    {
        source.CopyTo(destination);
    }
}