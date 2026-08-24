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
    //malformed or hostile frame and is refused before any item is decoded; the item-stream reader is handed this as
    //the per-item minimum so the client passes it at construction.
    internal const int MinTripleWireBytes = 3 * (1 + sizeof(int));

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

    /// <summary>Decodes one 16-byte content key from an item-stream request frame (low then high word, big-endian), advancing the cursor past it. A <see cref="Lumoin.Verisync.Core.DecodeItemDelegate{TItem}"/> the request channel binds; the key is a pure value, so it owns no pooled backing and returns no lease.</summary>
    /// <param name="reader">The cursor over the frame payload, positioned at the next key.</param>
    /// <param name="pool">The pool an owned item would rent from; unused, as a content key owns nothing.</param>
    /// <param name="lease">Always <see langword="null"/>: a content key views no pooled memory.</param>
    /// <returns>The decoded content key.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated part-way through the key.</exception>
    internal static ContentKey128 DecodeKey(ref SequenceReader<byte> reader, MemoryPool<byte> pool, out IDisposable? lease)
    {
        lease = null;
        if(!reader.TryReadBigEndian(out long low) || !reader.TryReadBigEndian(out long high))
        {
            throw new InvalidDataException("A content-hash triple-fetch frame is truncated.");
        }

        return new ContentKey128((ulong)low, (ulong)high);
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

    /// <summary>Decodes one triple from an item-stream response frame into terms backed by a single pooled rental, advancing the cursor past it. A <see cref="Lumoin.Verisync.Core.DecodeItemDelegate{TItem}"/> the response channel binds. The terms' <see cref="Utf8String"/> memory views the rental returned through <paramref name="lease"/>; the reader disposes it once the item has been handled, so the triple is valid only for that call.</summary>
    /// <param name="reader">The cursor over the frame payload, positioned at the next triple.</param>
    /// <param name="pool">The pool the triple's term-content bytes are rented from.</param>
    /// <param name="lease">The single rental backing the triple's terms, or <see langword="null"/> when the triple's three terms carry no content bytes.</param>
    /// <returns>The decoded triple, valid only until the reader disposes <paramref name="lease"/>.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, or carries an unknown term kind or base-direction byte.</exception>
    internal static ContentTriple DecodeTriple(ref SequenceReader<byte> reader, MemoryPool<byte> pool, out IDisposable? lease)
    {
        //Measure the one triple's total term-content bytes by walking a by-value copy of the cursor with the same
        //bounds checks the decode applies, so the exact rental size is known before a byte is copied.
        SequenceReader<byte> peek = reader;
        int contentBytes = MeasureTripleContentBytes(ref peek);

        if(contentBytes == 0)
        {
            //Three terms with no content bytes own no pooled backing.
            lease = null;

            return DecodeTripleInto(ref reader, Memory<byte>.Empty);
        }

        IMemoryOwner<byte> owner = pool.Rent(contentBytes);

        //The rental is disposed before any throw so a rejected item leaks nothing, per the item-decoder contract.
        try
        {
            ContentTriple triple = DecodeTripleInto(ref reader, owner.Memory[..contentBytes]);
            lease = owner;

            return triple;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    /// <summary>Sums the term-content byte lengths of one triple's three terms, applying the same bounds checks the decode does, and advances the peek cursor past the triple.</summary>
    /// <param name="peek">A by-value copy of the frame cursor, advanced past the triple.</param>
    /// <returns>The total term-content bytes the triple's terms carry.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, or carries an unknown term kind or base-direction byte.</exception>
    private static int MeasureTripleContentBytes(ref SequenceReader<byte> peek)
    {
        int total = 0;
        for(int termIndex = 0; termIndex < 3; termIndex++)
        {
            total += MeasureTermContentBytes(ref peek);
        }

        return total;
    }

    /// <summary>Sums the content byte lengths of one term's fields and advances the peek cursor past the term, refusing an unknown kind or base-direction byte exactly as the decode does.</summary>
    /// <param name="peek">A by-value copy of the frame cursor, advanced past the term.</param>
    /// <returns>The term's content byte length.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, or carries an unknown term kind or base-direction byte.</exception>
    private static int MeasureTermContentBytes(ref SequenceReader<byte> peek)
    {
        byte tag = ReadByteOrThrow(ref peek);

        switch(tag)
        {
            case IriTag:
            {
                return MeasureField(ref peek);
            }

            case LiteralTag:
            {
                int total = MeasureField(ref peek);
                total += MeasureField(ref peek);
                if(ReadByteOrThrow(ref peek) == 1)
                {
                    total += MeasureField(ref peek);
                }

                _ = ReadDirection(ReadByteOrThrow(ref peek));

                return total;
            }

            default:
            {
                throw new InvalidDataException($"A content-hash triple frame carried an unknown term tag {tag}.");
            }
        }
    }

    /// <summary>Reads a length-prefixed field's declared length, bounds it against the bytes present, and advances the peek cursor past the field's content.</summary>
    /// <param name="peek">A by-value copy of the frame cursor, advanced past the field.</param>
    /// <returns>The field's content byte length.</returns>
    /// <exception cref="InvalidDataException">The frame is too short for the declared field length.</exception>
    private static int MeasureField(ref SequenceReader<byte> peek)
    {
        int length = ReadCount(ref peek);
        if(length > peek.Remaining)
        {
            throw new InvalidDataException("A content-hash triple-fetch frame is truncated.");
        }

        peek.Advance(length);

        return length;
    }

    /// <summary>Decodes one triple's three terms from the cursor, copying each term's content bytes into consecutive slices of <paramref name="rental"/> and viewing them as the terms' UTF-8 strings.</summary>
    /// <param name="reader">The frame cursor, advanced past the triple.</param>
    /// <param name="rental">The pooled buffer, exactly the triple's total term-content bytes, the terms' UTF-8 strings view.</param>
    /// <returns>The decoded triple, its terms backed by <paramref name="rental"/>.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, or carries an unknown term kind or base-direction byte.</exception>
    private static ContentTriple DecodeTripleInto(ref SequenceReader<byte> reader, Memory<byte> rental)
    {
        int offset = 0;
        RdfTerm subject = DecodeTerm(ref reader, rental, ref offset);
        RdfTerm predicate = DecodeTerm(ref reader, rental, ref offset);
        RdfTerm @object = DecodeTerm(ref reader, rental, ref offset);

        return new ContentTriple(subject, predicate, @object);
    }

    /// <summary>Decodes one term from its kind tag and length-prefixed content fields, copying each field's bytes into <paramref name="rental"/> at <paramref name="offset"/> and viewing them.</summary>
    /// <param name="reader">The frame cursor, advanced past the term.</param>
    /// <param name="rental">The pooled buffer the term's UTF-8 strings view.</param>
    /// <param name="offset">The running write position in <paramref name="rental"/>, advanced past this term's content.</param>
    /// <returns>The term.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, or carries an unknown term kind or base-direction byte.</exception>
    private static RdfTerm DecodeTerm(ref SequenceReader<byte> reader, Memory<byte> rental, ref int offset)
    {
        byte tag = ReadByteOrThrow(ref reader);

        switch(tag)
        {
            case IriTag:
            {
                return new NamedNode(DecodeField(ref reader, rental, ref offset));
            }

            case LiteralTag:
            {
                Utf8String value = DecodeField(ref reader, rental, ref offset);
                NamedNode datatype = new(DecodeField(ref reader, rental, ref offset));
                Utf8String? language = ReadByteOrThrow(ref reader) == 1 ? DecodeField(ref reader, rental, ref offset) : null;
                TextDirection? direction = ReadDirection(ReadByteOrThrow(ref reader));

                return new Literal(value, datatype, language, direction);
            }

            default:
            {
                throw new InvalidDataException($"A content-hash triple frame carried an unknown term tag {tag}.");
            }
        }
    }

    /// <summary>Reads a length-prefixed field, copying its bytes into <paramref name="rental"/> at <paramref name="offset"/> and returning a UTF-8 string viewing that slice.</summary>
    /// <param name="reader">The frame cursor, advanced past the field.</param>
    /// <param name="rental">The pooled buffer the field's bytes are copied into.</param>
    /// <param name="offset">The write position in <paramref name="rental"/>, advanced past the field's content.</param>
    /// <returns>A UTF-8 string viewing the copied bytes.</returns>
    /// <exception cref="InvalidDataException">The frame is too short for the declared field length.</exception>
    private static Utf8String DecodeField(ref SequenceReader<byte> reader, Memory<byte> rental, ref int offset)
    {
        int length = ReadCount(ref reader);
        if(length > reader.Remaining)
        {
            throw new InvalidDataException("A content-hash triple-fetch frame is truncated.");
        }

        Memory<byte> slice = rental.Slice(offset, length);
        if(!reader.TryCopyTo(slice.Span))
        {
            throw new InvalidDataException("A content-hash triple-fetch frame is truncated.");
        }

        reader.Advance(length);
        offset += length;

        return new Utf8String(slice);
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
    /// <param name="reader">The frame reader, advanced past the count.</param>
    /// <returns>The count.</returns>
    /// <exception cref="InvalidDataException">The frame is too short or the count is negative.</exception>
    private static int ReadCount(ref SequenceReader<byte> reader)
    {
        if(!reader.TryReadBigEndian(out int count))
        {
            throw new InvalidDataException("A content-hash triple-fetch frame is truncated.");
        }

        if(count < 0)
        {
            throw new InvalidDataException("A content-hash triple-fetch frame declared a negative count.");
        }

        return count;
    }

    /// <summary>Reads a single byte, refusing a truncated frame.</summary>
    /// <param name="reader">The frame reader, advanced past the byte.</param>
    /// <returns>The byte.</returns>
    /// <exception cref="InvalidDataException">The frame holds no more bytes.</exception>
    private static byte ReadByteOrThrow(ref SequenceReader<byte> reader)
    {
        if(!reader.TryRead(out byte value))
        {
            throw new InvalidDataException("A content-hash triple-fetch frame is truncated.");
        }

        return value;
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
