using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The per-message serializers the content-hash triple-fetch transport binds to a Verisync message channel: a
/// request carries the peer-only content-hash items to resolve, a response carries the triples the peer holds for
/// them as terms (dictionary-independent, so each side re-encodes into its own dictionary). The channel adds the
/// length-prefix framing; these turn one message into bytes and back. Terms are encoded kind-tagged with
/// length-prefixed content fields — the same shape the content-hash projection hashes, but here read back into
/// terms. Only IRIs and literals appear (the content-hash domain rejects blank nodes and RDF 1.2 triple terms), so
/// those are the only kinds encoded; anything else is a malformed frame.
/// </summary>
internal static class ContentTripleFraming
{
    private const byte IriTag = (byte)'I';
    private const byte LiteralTag = (byte)'L';
    private const byte NoDirection = 0;
    private const byte LtrDirection = 1;
    private const byte RtlDirection = 2;

    //The fewest bytes one triple can occupy on the wire: three terms, each at least a one-byte kind tag and a
    //four-byte zero-length content field. A declared triple count beyond what the frame can hold at this floor is a
    //malformed or hostile frame and is refused before an array is sized from it.
    private const int MinTripleWireBytes = 3 * (1 + sizeof(int));

    /// <summary>Writes a triple-fetch request: the item count followed by each 16-byte content key (low then high word, big-endian).</summary>
    /// <param name="items">The content-hash items to fetch triples for.</param>
    /// <param name="output">The channel buffer to write into.</param>
    internal static void WriteKeys(IReadOnlyList<ContentKey128> items, IBufferWriter<byte> output)
    {
        WriteCount(output, items.Count);
        foreach(ContentKey128 item in items)
        {
            Span<byte> span = output.GetSpan(ContentKey128.ByteWidth);
            BinaryPrimitives.WriteUInt64BigEndian(span, item.Low);
            BinaryPrimitives.WriteUInt64BigEndian(span[sizeof(ulong)..], item.High);
            output.Advance(ContentKey128.ByteWidth);
        }
    }

    /// <summary>Reads a triple-fetch request — the item count then that many 16-byte content keys.</summary>
    /// <param name="payload">The framed request payload.</param>
    /// <returns>The requested content-hash items.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated or its count does not match its length.</exception>
    internal static IReadOnlyList<ContentKey128> ReadKeys(ReadOnlySequence<byte> payload)
    {
        byte[] buffer = payload.ToArray();
        int position = 0;
        int count = ReadCount(buffer, ref position);
        if((long)count * ContentKey128.ByteWidth > buffer.Length - position)
        {
            throw new InvalidDataException("A content-hash triple-fetch request declared more items than its frame can hold.");
        }

        ContentKey128[] items = new ContentKey128[count];
        for(int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> key = Take(buffer, ref position, ContentKey128.ByteWidth);
            items[i] = new ContentKey128(BinaryPrimitives.ReadUInt64BigEndian(key), BinaryPrimitives.ReadUInt64BigEndian(key[sizeof(ulong)..]));
        }

        return items;
    }

    /// <summary>Writes a triple-fetch response: the triple count followed by each triple's three terms.</summary>
    /// <param name="triples">The triples to send, as terms.</param>
    /// <param name="output">The channel buffer to write into.</param>
    /// <exception cref="NotSupportedException">A term is not an IRI or a literal.</exception>
    internal static void WriteTriples(IReadOnlyList<ContentTriple> triples, IBufferWriter<byte> output)
    {
        WriteCount(output, triples.Count);
        foreach(ContentTriple triple in triples)
        {
            WriteTerm(triple.Subject, output);
            WriteTerm(triple.Predicate, output);
            WriteTerm(triple.Object, output);
        }
    }

    /// <summary>Reads a triple-fetch response — the triple count then that many triples, each three terms.</summary>
    /// <param name="payload">The framed response payload.</param>
    /// <returns>The peer's triples, as terms.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, mis-counted, or carries an unknown term kind.</exception>
    internal static IReadOnlyList<ContentTriple> ReadTriples(ReadOnlySequence<byte> payload)
    {
        byte[] buffer = payload.ToArray();
        int position = 0;
        int count = ReadCount(buffer, ref position);
        if((long)count * MinTripleWireBytes > buffer.Length - position)
        {
            throw new InvalidDataException("A content-hash triple-fetch response declared more triples than its frame can hold.");
        }

        ContentTriple[] triples = new ContentTriple[count];
        for(int i = 0; i < count; i++)
        {
            RdfTerm subject = ReadTerm(buffer, ref position);
            RdfTerm predicate = ReadTerm(buffer, ref position);
            RdfTerm @object = ReadTerm(buffer, ref position);
            triples[i] = new ContentTriple(subject, predicate, @object);
        }

        return triples;
    }

    /// <summary>Writes a non-negative count as a four-byte big-endian integer.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="count">The count.</param>
    private static void WriteCount(IBufferWriter<byte> output, int count)
    {
        Span<byte> span = output.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(span, count);
        output.Advance(sizeof(int));
    }

    /// <summary>Reads and validates a non-negative four-byte big-endian count.</summary>
    /// <param name="buffer">The frame bytes.</param>
    /// <param name="position">The read position, advanced past the count.</param>
    /// <returns>The count.</returns>
    /// <exception cref="InvalidDataException">The frame is too short or the count is negative.</exception>
    private static int ReadCount(byte[] buffer, ref int position)
    {
        int count = BinaryPrimitives.ReadInt32BigEndian(Take(buffer, ref position, sizeof(int)));
        if(count < 0)
        {
            throw new InvalidDataException("A content-hash triple-fetch frame declared a negative count.");
        }

        return count;
    }

    /// <summary>Writes a term: its kind tag and length-prefixed content fields.</summary>
    /// <param name="term">The term to write.</param>
    /// <param name="output">The channel buffer to write into.</param>
    /// <exception cref="NotSupportedException">The term is not an IRI or a literal.</exception>
    private static void WriteTerm(RdfTerm term, IBufferWriter<byte> output)
    {
        switch(term)
        {
            case NamedNode named:
            {
                WriteByte(output, IriTag);
                WriteField(output, named.Iri.Span);
                break;
            }

            case Literal literal:
            {
                WriteByte(output, LiteralTag);
                WriteField(output, literal.Value.Span);
                WriteField(output, literal.Datatype.Iri.Span);
                if(literal.Language is { } language)
                {
                    WriteByte(output, 1);
                    WriteField(output, language.Span);
                }
                else
                {
                    WriteByte(output, 0);
                }

                WriteByte(output, DirectionByte(literal.BaseDirection));
                break;
            }

            default:
            {
                throw new NotSupportedException($"The content-hash triple wire format carries only IRIs and literals, not '{term.GetType().Name}'.");
            }
        }
    }

    /// <summary>Reads a term from its kind tag and length-prefixed content fields.</summary>
    /// <param name="buffer">The frame bytes.</param>
    /// <param name="position">The read position, advanced past the term.</param>
    /// <returns>The term.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated or carries an unknown term kind or base-direction byte.</exception>
    private static RdfTerm ReadTerm(byte[] buffer, ref int position)
    {
        byte tag = Take(buffer, ref position, 1)[0];

        return tag switch
        {
            IriTag => new NamedNode(ReadUtf8(buffer, ref position)),
            LiteralTag => ReadLiteral(buffer, ref position),
            _ => throw new InvalidDataException($"A content-hash triple frame carried an unknown term tag {tag}."),
        };
    }

    /// <summary>Reads a literal: its length-prefixed value and datatype IRI, the language presence byte and (when present) its bytes, then the base-direction byte.</summary>
    /// <param name="buffer">The frame bytes.</param>
    /// <param name="position">The read position, advanced past the literal's fields.</param>
    /// <returns>The literal.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated or carries an unknown base-direction byte.</exception>
    private static Literal ReadLiteral(byte[] buffer, ref int position)
    {
        Utf8String value = ReadUtf8(buffer, ref position);
        NamedNode datatype = new(ReadUtf8(buffer, ref position));
        Utf8String? language = Take(buffer, ref position, 1)[0] == 1 ? ReadUtf8(buffer, ref position) : null;
        TextDirection? direction = ReadDirection(Take(buffer, ref position, 1)[0]);

        return new Literal(value, datatype, language, direction);
    }

    /// <summary>Writes a single byte.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="value">The byte.</param>
    private static void WriteByte(IBufferWriter<byte> output, byte value)
    {
        Span<byte> span = output.GetSpan(1);
        span[0] = value;
        output.Advance(1);
    }

    /// <summary>Writes a length-prefixed field: a four-byte big-endian length then the content bytes.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="content">The content bytes.</param>
    private static void WriteField(IBufferWriter<byte> output, ReadOnlySpan<byte> content)
    {
        WriteCount(output, content.Length);
        output.Write(content);
    }

    /// <summary>Reads a length-prefixed field's bytes into an owned <see cref="Utf8String"/>.</summary>
    /// <param name="buffer">The frame bytes.</param>
    /// <param name="position">The read position, advanced past the field.</param>
    /// <returns>The field's bytes as a UTF-8 string.</returns>
    /// <exception cref="InvalidDataException">The frame is too short for the declared field length.</exception>
    private static Utf8String ReadUtf8(byte[] buffer, ref int position)
    {
        int length = ReadCount(buffer, ref position);

        return new Utf8String(Take(buffer, ref position, length).ToArray());
    }

    /// <summary>Returns a slice of the frame and advances the position, refusing to read past the end.</summary>
    /// <param name="buffer">The frame bytes.</param>
    /// <param name="position">The read position, advanced by <paramref name="length"/>.</param>
    /// <param name="length">The number of bytes to take.</param>
    /// <returns>The slice.</returns>
    /// <exception cref="InvalidDataException">The frame is shorter than the requested slice.</exception>
    private static ReadOnlySpan<byte> Take(byte[] buffer, ref int position, int length)
    {
        if(length < 0 || (long)position + length > buffer.Length)
        {
            throw new InvalidDataException("A content-hash triple-fetch frame is truncated.");
        }

        ReadOnlySpan<byte> slice = buffer.AsSpan(position, length);
        position += length;

        return slice;
    }

    /// <summary>The wire byte for a literal's optional base direction.</summary>
    /// <param name="direction">The base direction, or <see langword="null"/>.</param>
    /// <returns>0 for none, 1 for left-to-right, 2 for right-to-left.</returns>
    private static byte DirectionByte(TextDirection? direction)
    {
        return direction switch
        {
            null => NoDirection,
            TextDirection.Ltr => LtrDirection,
            _ => RtlDirection,
        };
    }

    /// <summary>The base direction a wire byte denotes.</summary>
    /// <param name="value">The wire byte.</param>
    /// <returns>The base direction, or <see langword="null"/> for none.</returns>
    /// <exception cref="InvalidDataException">The byte is not a known base-direction value.</exception>
    private static TextDirection? ReadDirection(byte value)
    {
        return value switch
        {
            NoDirection => null,
            LtrDirection => TextDirection.Ltr,
            RtlDirection => TextDirection.Rtl,
            _ => throw new InvalidDataException($"A content-hash triple frame carried an unknown base-direction byte {value}."),
        };
    }
}
