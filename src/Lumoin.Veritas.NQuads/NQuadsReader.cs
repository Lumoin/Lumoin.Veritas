using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.NQuads;

/// <summary>
/// Parses N-Quads and N-Triples format into <see cref="Quad"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// N-Quads is a line-oriented format defined at https://www.w3.org/TR/n-quads/.
/// Each non-empty, non-comment line contains exactly one statement in the form:
/// <c>subject predicate object [graph] .</c>
/// </para>
/// <para>
/// The parser operates on UTF-8 bytes directly. Term strings are interned via
/// <see cref="Utf8StringPool"/> to deduplicate repeated IRIs (such as
/// <c>rdf:type</c>) without per-term heap allocation.
/// </para>
/// <para>
/// Lines are read one at a time. The parser does not buffer the entire input.
/// For large files this means memory usage is bounded by the longest single line.
/// </para>
/// </remarks>
public static class NQuadsReader
{
    private const byte Hash = (byte)'#';
    private const byte LessThan = (byte)'<';
    private const byte GreaterThan = (byte)'>';
    private const byte BlankNodeUnderscore = (byte)'_';
    private const byte BlankNodeColon = (byte)':';
    private const byte QuoteByte = (byte)'"';
    private const byte AtSign = (byte)'@';
    private const byte Caret = (byte)'^';
    private const byte Period = (byte)'.';

    /// <summary>
    /// Parses N-Quads from a stream, yielding one <see cref="Quad"/> per statement.
    /// </summary>
    /// <param name="stream">The UTF-8 encoded N-Quads input stream.</param>
    /// <param name="pool">
    /// The pool used for interning term strings. If <c>null</c>, a temporary pool
    /// is created and disposed when enumeration ends.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    /// <exception cref="NQuadsParseException">
    /// A line was encountered that could not be parsed as a valid N-Quads statement.
    /// </exception>
    public static IAsyncEnumerable<Quad> ReadAsync(
        Stream stream,
        Utf8StringPool? pool = null,
        CancellationToken cancellationToken = default)
    {
        return ParseAsync(stream, pool, cancellationToken);
    }

    /// <summary>
    /// Parses N-Quads from a <see cref="ReadOnlyMemory{T}"/> buffer.
    /// </summary>
    /// <param name="nquads">The UTF-8 encoded N-Quads data.</param>
    /// <param name="pool">
    /// The pool used for interning term strings. If <c>null</c>, a temporary pool
    /// is created and disposed when enumeration ends.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    public static IAsyncEnumerable<Quad> ReadAsync(
        ReadOnlyMemory<byte> nquads,
        Utf8StringPool? pool = null,
        CancellationToken cancellationToken = default)
    {
        MemoryStream stream = new(nquads.ToArray(), writable: false);
        return ParseAsync(stream, pool, cancellationToken);
    }

    private static async IAsyncEnumerable<Quad> ParseAsync(
        Stream stream,
        Utf8StringPool? externalPool,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //Declare before try so the finally block can always reach it.
        Utf8StringPool? ownedPool = null;

        try
        {
            //If the caller supplied a pool, use it directly and leave ownedPool null
            //so the finally block does not dispose what we do not own.
            Utf8StringPool pool = externalPool ?? (ownedPool = new Utf8StringPool());

            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            int lineNumber = 0;
            string? line;

            while((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                ReadOnlySpan<byte> lineBytes = Encoding.UTF8.GetBytes(line);
                ReadOnlySpan<byte> trimmed = lineBytes.Trim((byte)' ').Trim((byte)'\t');

                //Skip empty lines and comments.
                if(trimmed.IsEmpty || trimmed[0] == Hash)
                {
                    continue;
                }

                Quad quad = ParseLine(trimmed, pool, lineNumber);
                yield return quad;
            }
        }
        finally
        {
            ownedPool?.Dispose();
        }
    }

    /// <summary>
    /// Parses a single non-empty, non-comment N-Quads line.
    /// </summary>
    private static Quad ParseLine(ReadOnlySpan<byte> line, Utf8StringPool pool, int lineNumber)
    {
        int pos = 0;

        SkipWhitespace(line, ref pos);
        RdfTerm subject = ParseSubject(line, ref pos, pool, lineNumber);

        SkipWhitespace(line, ref pos);
        NamedNode predicate = ParsePredicate(line, ref pos, pool, lineNumber);

        SkipWhitespace(line, ref pos);
        RdfTerm @object = ParseObject(line, ref pos, pool, lineNumber);

        SkipWhitespace(line, ref pos);

        //Optional graph name.
        RdfTerm? graph = null;
        if(pos < line.Length && line[pos] != Period)
        {
            graph = ParseGraphName(line, ref pos, pool, lineNumber);
            SkipWhitespace(line, ref pos);
        }

        //Expect the statement terminator.
        if(pos >= line.Length || line[pos] != Period)
        {
            throw new NQuadsParseException(
                $"Expected '.' at end of statement on line {lineNumber}.", lineNumber);
        }

        return new Quad(subject, predicate, @object, graph);
    }

    private static RdfTerm ParseSubject(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line while reading subject on line {lineNumber}.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI or blank node for subject on line {lineNumber}.", lineNumber)
        };
    }

    private static NamedNode ParsePredicate(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length || line[pos] != LessThan)
        {
            throw new NQuadsParseException(
                $"Expected IRI for predicate on line {lineNumber}.", lineNumber);
        }

        return (NamedNode)ParseIriRef(line, ref pos, pool, lineNumber);
    }

    private static RdfTerm ParseObject(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line while reading object on line {lineNumber}.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            QuoteByte => ParseLiteral(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI, blank node, or literal for object on line {lineNumber}.", lineNumber)
        };
    }

    private static RdfTerm ParseGraphName(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line while reading graph name on line {lineNumber}.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI or blank node for graph name on line {lineNumber}.", lineNumber)
        };
    }

    /// <summary>
    /// Parses an IRI reference of the form <c>&lt;iri&gt;</c>.
    /// </summary>
    private static NamedNode ParseIriRef(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        //Skip '<'.
        pos++;

        int start = pos;
        while(pos < line.Length && line[pos] != GreaterThan)
        {
            pos++;
        }

        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unterminated IRI on line {lineNumber}.", lineNumber);
        }

        ReadOnlySpan<byte> iriBytes = line[start..pos];
        Utf8String iri = pool.Intern(UnescapeIri(iriBytes));

        //Skip '>'.
        pos++;

        return new NamedNode(iri);
    }

    /// <summary>
    /// Parses a blank node of the form <c>_:label</c>.
    /// </summary>
    private static BlankNode ParseBlankNode(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        //Expect '_:'.
        if(pos + 1 >= line.Length || line[pos] != BlankNodeUnderscore || line[pos + 1] != BlankNodeColon)
        {
            throw new NQuadsParseException($"Invalid blank node syntax on line {lineNumber}.", lineNumber);
        }

        pos += 2;

        int start = pos;
        while(pos < line.Length && !IsWhitespace(line[pos]) && line[pos] != Period)
        {
            pos++;
        }

        if(pos == start)
        {
            throw new NQuadsParseException($"Empty blank node label on line {lineNumber}.", lineNumber);
        }

        Utf8String label = pool.Intern(line[start..pos]);
        return new BlankNode(label);
    }

    /// <summary>
    /// Parses a literal of the form <c>"value"</c>, <c>"value"@lang</c>, or <c>"value"^^&lt;datatype&gt;</c>.
    /// </summary>
    private static Literal ParseLiteral(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        //Skip opening quote.
        pos++;

        //Read the quoted value, handling escape sequences.
        byte[] valueBuffer = ArrayPool<byte>.Shared.Rent(line.Length);
        try
        {
            int valueLength = ReadQuotedString(line, ref pos, valueBuffer, lineNumber);
            Utf8String value = pool.Intern(valueBuffer.AsSpan(0, valueLength));

            //Skip closing quote.
            if(pos >= line.Length || line[pos] != QuoteByte)
            {
                throw new NQuadsParseException($"Unterminated string literal on line {lineNumber}.", lineNumber);
            }

            pos++;

            //Check for language tag or datatype.
            if(pos < line.Length && line[pos] == AtSign)
            {
                //Language tag: @lang.
                pos++;
                int langStart = pos;
                while(pos < line.Length && !IsWhitespace(line[pos]) && line[pos] != Period)
                {
                    pos++;
                }

                Utf8String language = pool.Intern(line[langStart..pos]);
                NamedNode langStringType = new(pool.Intern(Vocabulary.Rdf.LangString.Span));
                return new Literal(value, langStringType, language);
            }
            else if(pos + 1 < line.Length && line[pos] == Caret && line[pos + 1] == Caret)
            {
                //Datatype: ^^<IRI>.
                pos += 2;

                if(pos >= line.Length || line[pos] != LessThan)
                {
                    throw new NQuadsParseException($"Expected IRI after '^^' on line {lineNumber}.", lineNumber);
                }

                NamedNode datatype = ParseIriRef(line, ref pos, pool, lineNumber);
                return new Literal(value, datatype);
            }
            else
            {
                //Plain literal: implicitly xsd:string.
                NamedNode xsdString = new(pool.Intern(Vocabulary.Xsd.String.Span));
                return new Literal(value, xsdString);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(valueBuffer);
        }
    }

    /// <summary>
    /// Reads a quoted string body, processing N-Quads escape sequences.
    /// Returns the number of bytes written to <paramref name="output"/>.
    /// </summary>
    private static int ReadQuotedString(ReadOnlySpan<byte> line, ref int pos, byte[] output, int lineNumber)
    {
        int outPos = 0;

        while(pos < line.Length && line[pos] != QuoteByte)
        {
            if(line[pos] == (byte)'\\')
            {
                pos++;
                if(pos >= line.Length)
                {
                    throw new NQuadsParseException($"Unterminated escape sequence on line {lineNumber}.", lineNumber);
                }

                switch((char)line[pos])
                {
                    case 't':
                        output[outPos++] = (byte)'\t';
                        pos++;
                        break;
                    case 'n':
                        output[outPos++] = (byte)'\n';
                        pos++;
                        break;
                    case 'r':
                        output[outPos++] = (byte)'\r';
                        pos++;
                        break;
                    case '"':
                        output[outPos++] = (byte)'"';
                        pos++;
                        break;
                    case '\\':
                        output[outPos++] = (byte)'\\';
                        pos++;
                        break;
                    case 'u':
                    {
                        pos++;
                        if(pos + 4 > line.Length)
                        {
                            throw new NQuadsParseException($"Incomplete \\uXXXX escape on line {lineNumber}.", lineNumber);
                        }

                        int codePoint = ParseHex4(line[pos..(pos + 4)], lineNumber);
                        pos += 4;
                        outPos += EncodeUtf8CodePoint(codePoint, output.AsSpan(outPos));
                        break;
                    }
                    case 'U':
                    {
                        pos++;
                        if(pos + 8 > line.Length)
                        {
                            throw new NQuadsParseException($"Incomplete \\UXXXXXXXX escape on line {lineNumber}.", lineNumber);
                        }

                        int codePoint = ParseHex8(line[pos..(pos + 8)], lineNumber);
                        pos += 8;
                        outPos += EncodeUtf8CodePoint(codePoint, output.AsSpan(outPos));
                        break;
                    }
                    default:
                        throw new NQuadsParseException(
                            $"Unknown escape sequence '\\{(char)line[pos]}' on line {lineNumber}.", lineNumber);
                }
            }
            else
            {
                output[outPos++] = line[pos++];
            }
        }

        return outPos;
    }

    private static ReadOnlySpan<byte> UnescapeIri(ReadOnlySpan<byte> iri)
    {
        //Check if any unescaping is needed.
        foreach(byte b in iri)
        {
            if(b == (byte)'\\')
            {
                return UnescapeIriSlow(iri);
            }
        }

        return iri;
    }

    private static byte[] UnescapeIriSlow(ReadOnlySpan<byte> iri)
    {
        byte[] output = new byte[iri.Length * 2];
        int outPos = 0;
        int pos = 0;

        while(pos < iri.Length)
        {
            if(iri[pos] == (byte)'\\' && pos + 1 < iri.Length && iri[pos + 1] == (byte)'u')
            {
                pos += 2;
                int codePoint = ParseHex4(iri[pos..(pos + 4)], 0);
                pos += 4;
                outPos += EncodeUtf8CodePoint(codePoint, output.AsSpan(outPos));
            }
            else
            {
                output[outPos++] = iri[pos++];
            }
        }

        return output[..outPos];
    }

    private static int ParseHex4(ReadOnlySpan<byte> hex, int lineNumber)
    {
        return (HexDigit(hex[0], lineNumber) << 12)
             | (HexDigit(hex[1], lineNumber) << 8)
             | (HexDigit(hex[2], lineNumber) << 4)
             | HexDigit(hex[3], lineNumber);
    }

    private static int ParseHex8(ReadOnlySpan<byte> hex, int lineNumber)
    {
        return (HexDigit(hex[0], lineNumber) << 28)
             | (HexDigit(hex[1], lineNumber) << 24)
             | (HexDigit(hex[2], lineNumber) << 20)
             | (HexDigit(hex[3], lineNumber) << 16)
             | (HexDigit(hex[4], lineNumber) << 12)
             | (HexDigit(hex[5], lineNumber) << 8)
             | (HexDigit(hex[6], lineNumber) << 4)
             | HexDigit(hex[7], lineNumber);
    }

    private static int HexDigit(byte b, int lineNumber)
    {
        return b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - '0',
            >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
            _ => throw new NQuadsParseException($"Invalid hex digit '{(char)b}' on line {lineNumber}.", lineNumber)
        };
    }

    private static int EncodeUtf8CodePoint(int codePoint, Span<byte> output)
    {
        return Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint).AsSpan(), output);
    }

    private static void SkipWhitespace(ReadOnlySpan<byte> line, ref int pos)
    {
        while(pos < line.Length && IsWhitespace(line[pos]))
        {
            pos++;
        }
    }

    private static bool IsWhitespace(byte b)
    {
        return b is (byte)' ' or (byte)'\t';
    }
}