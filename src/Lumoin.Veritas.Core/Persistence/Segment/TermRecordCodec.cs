using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Parsing;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// The per-<see cref="RdfTerm"/> wire codec: the size, write, and read of one term record, shared by every
/// artifact that persists RDF terms. A triple term is encoded <b>inline</b> — its component terms written in
/// full, walked with an explicit stack, never recursion — because a term dictionary interns a triple term
/// without interning its components, so a component is not guaranteed to carry its own identifier. Both the
/// <see cref="DictionarySegment"/> blocks and the durable dataset journal's term sections frame their terms
/// through this one codec, so the two on-disk forms encode a term identically byte for byte.
/// </summary>
internal static class TermRecordCodec
{
    /// <summary>The record tag for a <see cref="NamedNode"/>.</summary>
    private const byte NamedTag = 1;

    /// <summary>The record tag for a <see cref="BlankNode"/>.</summary>
    private const byte BlankTag = 2;

    /// <summary>The record tag for a <see cref="Literal"/>.</summary>
    private const byte LiteralTag = 3;

    /// <summary>The record tag for a <see cref="TripleTerm"/>; its three components follow inline.</summary>
    private const byte TripleTag = 4;

    /// <summary>The record tag for an <see cref="EngineNode"/>; the family code and the four key components follow as a fixed-width payload.</summary>
    private const byte EngineTag = 5;

    /// <summary>The byte width of an <see cref="EngineNode"/> record: the tag, the family code, and the four little-endian 32-bit key components.</summary>
    private const int EngineRecordWidth = 1 + 1 + (4 * sizeof(uint));

    /// <summary>The literal base-direction byte for no direction.</summary>
    private const byte NoDirection = 0;

    /// <summary>The literal base-direction byte for left-to-right.</summary>
    private const byte LtrDirection = 1;

    /// <summary>The literal base-direction byte for right-to-left.</summary>
    private const byte RtlDirection = 2;

    /// <summary>The literal language-presence byte when no language tag is present.</summary>
    private const byte LanguageAbsent = 0;

    /// <summary>The literal language-presence byte when a language tag is present and its length-prefixed bytes follow.</summary>
    private const byte LanguagePresent = 1;

    /// <summary>The byte width of a length prefix on a variable-length field.</summary>
    private const int LengthPrefixWidth = sizeof(uint);

    /// <summary>The number of bytes <see cref="Write"/> writes for a term, including any inline triple-term components, walked iteratively with <paramref name="work"/> (left empty on return).</summary>
    /// <param name="root">The term to measure.</param>
    /// <param name="work">A reusable, empty work stack for the iterative walk; empty again on return.</param>
    /// <returns>The encoded byte count.</returns>
    internal static int ComputeSize(RdfTerm root, Stack<RdfTerm> work)
    {
        int size = 0;
        work.Push(root);
        while(work.Count > 0)
        {
            RdfTerm term = work.Pop();
            if(term is TripleTerm triple)
            {
                size += 1;
                work.Push(triple.Subject);
                work.Push(triple.Predicate);
                work.Push(triple.Object);

                continue;
            }

            size += term switch
            {
                NamedNode named => 1 + LengthPrefixWidth + named.Iri.Span.Length,
                BlankNode blank => 1 + LengthPrefixWidth + blank.Label.Span.Length,
                Literal literal => LiteralEncodedSize(literal),
                EngineNode => EngineRecordWidth,
                _ => throw UnsupportedTerm(term),
            };
        }

        return size;
    }

    /// <summary>Writes a term — and any inline triple-term components, in pre-order — into <paramref name="destination"/>, walked iteratively with <paramref name="work"/> (left empty on return) so a deeply-nested term cannot overflow the call stack.</summary>
    /// <param name="root">The term to write.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <param name="work">A reusable, empty work stack for the iterative walk; empty again on return.</param>
    /// <returns>The number of bytes written.</returns>
    internal static int Write(RdfTerm root, Span<byte> destination, Stack<RdfTerm> work)
    {
        int p = 0;
        work.Push(root);
        while(work.Count > 0)
        {
            RdfTerm term = work.Pop();
            if(term is TripleTerm triple)
            {
                destination[p++] = TripleTag;

                //Push object, then predicate, then subject so the components pop and write in the order
                //subject, predicate, object after the tag — the pre-order a reader reconstructs from.
                work.Push(triple.Object);
                work.Push(triple.Predicate);
                work.Push(triple.Subject);

                continue;
            }

            p += term switch
            {
                NamedNode named => WriteNamed(named, destination[p..]),
                BlankNode blank => WriteBlank(blank, destination[p..]),
                Literal literal => WriteLiteral(literal, destination[p..]),
                EngineNode engine => WriteEngine(engine, destination[p..]),
                _ => throw UnsupportedTerm(term),
            };
        }

        return p;
    }

    /// <summary>Reads one term — and any inline triple-term components — from the start of <paramref name="source"/>, reconstructing nesting bottom-up with <paramref name="frames"/> (left empty on return) so a deeply-nested term cannot overflow the call stack. Term bytes are interned into <paramref name="pool"/>.</summary>
    /// <param name="source">The bytes positioned at a term record.</param>
    /// <param name="consumed">Receives the number of bytes the term record occupied.</param>
    /// <param name="pool">The pool the term bytes are interned into.</param>
    /// <param name="frames">A reusable, empty stack of partially-built triple-term frames; empty again on a clean return.</param>
    /// <returns>The reconstructed term.</returns>
    /// <exception cref="InvalidDataException">A record is truncated, carries an unknown tag, nests beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/>, or names a triple-term predicate that is not a named node.</exception>
    internal static RdfTerm Read(ReadOnlySpan<byte> source, out int consumed, Utf8StringPool pool, Stack<TripleFrame> frames)
    {
        int position = 0;
        while(true)
        {
            EnsureAvailable(source, position, 1);
            byte tag = source[position++];
            if(tag == TripleTag)
            {
                if(frames.Count >= QuotedTripleLimits.MaxNestingDepth)
                {
                    throw new InvalidDataException($"A term record nests a triple term beyond the depth limit {QuotedTripleLimits.MaxNestingDepth}.");
                }

                frames.Push(new TripleFrame());

                continue;
            }

            RdfTerm term = tag switch
            {
                NamedTag => new NamedNode(ReadField(source, ref position, pool)),
                BlankTag => new BlankNode(ReadField(source, ref position, pool)),
                LiteralTag => ReadLiteral(source, ref position, pool),
                EngineTag => ReadEngine(source, ref position),
                _ => throw new InvalidDataException($"A term record carries an unknown term tag {tag}."),
            };

            bool consumedByFrame = false;
            while(frames.Count > 0)
            {
                TripleFrame frame = frames.Peek();
                frame.Fill(term);
                if(!frame.IsComplete)
                {
                    consumedByFrame = true;

                    break;
                }

                frames.Pop();
                term = frame.Build();
            }

            if(consumedByFrame)
            {
                continue;
            }

            consumed = position;

            return term;
        }
    }

    /// <summary>The number of bytes <see cref="WriteLiteral"/> writes for a literal: the tag, the length-prefixed value and datatype IRI, the language-presence byte and (when present) its length-prefixed bytes, and the base-direction byte.</summary>
    /// <param name="literal">The literal.</param>
    /// <returns>The encoded byte count.</returns>
    private static int LiteralEncodedSize(Literal literal)
    {
        int languageSize = literal.Language is { } language ? LengthPrefixWidth + language.Span.Length : 0;

        return 1 + LengthPrefixWidth + literal.Value.Span.Length + LengthPrefixWidth + literal.Datatype.Iri.Span.Length + 1 + languageSize + 1;
    }

    /// <summary>Writes a named node: the named-node tag and the length-prefixed IRI bytes.</summary>
    /// <param name="named">The named node.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteNamed(NamedNode named, Span<byte> destination)
    {
        destination[0] = NamedTag;

        return 1 + WriteField(destination[1..], named.Iri.Span);
    }

    /// <summary>Writes a blank node: the blank-node tag and the length-prefixed label bytes.</summary>
    /// <param name="blank">The blank node.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteBlank(BlankNode blank, Span<byte> destination)
    {
        destination[0] = BlankTag;

        return 1 + WriteField(destination[1..], blank.Label.Span);
    }

    /// <summary>Writes a literal: the literal tag, the length-prefixed lexical value and datatype IRI, a language-presence byte and (when present) the length-prefixed language bytes, then the base-direction byte. The presence byte distinguishes an absent language from a present-but-empty one.</summary>
    /// <param name="literal">The literal.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteLiteral(Literal literal, Span<byte> destination)
    {
        destination[0] = LiteralTag;
        int p = 1;
        p += WriteField(destination[p..], literal.Value.Span);
        p += WriteField(destination[p..], literal.Datatype.Iri.Span);
        if(literal.Language is { } language)
        {
            destination[p++] = LanguagePresent;
            p += WriteField(destination[p..], language.Span);
        }
        else
        {
            destination[p++] = LanguageAbsent;
        }

        destination[p++] = DirectionByte(literal.BaseDirection);

        return p;
    }

    /// <summary>Writes an engine node: the engine tag, the family code, and the four little-endian 32-bit key components.</summary>
    /// <param name="engine">The engine node.</param>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteEngine(EngineNode engine, Span<byte> destination)
    {
        destination[0] = EngineTag;
        destination[1] = engine.Family.Code;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[2..], engine.Key0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], engine.Key1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[10..], engine.Key2);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[14..], engine.Key3);

        return EngineRecordWidth;
    }

    /// <summary>Reads an engine-node record: the family code and the four little-endian 32-bit key components. Rehydration reconstructs the content identity exactly — the engine node a replayed journal resolves is equal to the one the engine minted.</summary>
    /// <param name="source">The record bytes.</param>
    /// <param name="position">The read cursor, positioned after the tag; advanced past the consumed bytes.</param>
    /// <returns>The reconstructed engine node.</returns>
    /// <exception cref="InvalidDataException">The record is truncated.</exception>
    private static EngineNode ReadEngine(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureAvailable(source, position, EngineRecordWidth - 1);
        EngineNodeFamily family = EngineNodeFamily.Create(source[position]);
        uint key0 = BinaryPrimitives.ReadUInt32LittleEndian(source[(position + 1)..]);
        uint key1 = BinaryPrimitives.ReadUInt32LittleEndian(source[(position + 5)..]);
        uint key2 = BinaryPrimitives.ReadUInt32LittleEndian(source[(position + 9)..]);
        uint key3 = BinaryPrimitives.ReadUInt32LittleEndian(source[(position + 13)..]);
        position += EngineRecordWidth - 1;

        return new EngineNode(family, key0, key1, key2, key3);
    }

    /// <summary>Writes a length-prefixed field: a little-endian 32-bit length followed by the content bytes.</summary>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <param name="content">The content bytes.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteField(Span<byte> destination, ReadOnlySpan<byte> content)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)content.Length);
        content.CopyTo(destination[LengthPrefixWidth..]);

        return LengthPrefixWidth + content.Length;
    }

    /// <summary>The canonical byte for a literal's optional base direction.</summary>
    /// <param name="direction">The base direction, or <see langword="null"/>.</param>
    /// <returns><see cref="NoDirection"/>, <see cref="LtrDirection"/>, or <see cref="RtlDirection"/>.</returns>
    private static byte DirectionByte(TextDirection? direction)
    {
        return direction switch
        {
            null => NoDirection,
            TextDirection.Ltr => LtrDirection,
            _ => RtlDirection,
        };
    }

    /// <summary>Reads a literal record: the length-prefixed value and datatype IRI, the language-presence byte and (when present) its length-prefixed bytes, then the base-direction byte.</summary>
    /// <param name="source">The record bytes.</param>
    /// <param name="position">The read cursor; advanced past the consumed bytes.</param>
    /// <param name="pool">The pool the value, datatype IRI, and language bytes are interned into.</param>
    /// <returns>The reconstructed literal.</returns>
    /// <exception cref="InvalidDataException">A field is truncated, or the language-presence or base-direction byte is invalid.</exception>
    private static Literal ReadLiteral(ReadOnlySpan<byte> source, ref int position, Utf8StringPool pool)
    {
        Utf8String value = ReadField(source, ref position, pool);
        NamedNode datatype = new(ReadField(source, ref position, pool));

        EnsureAvailable(source, position, 1);
        byte languagePresence = source[position++];
        Utf8String? language = languagePresence switch
        {
            LanguagePresent => ReadField(source, ref position, pool),
            LanguageAbsent => null,
            _ => throw new InvalidDataException("A term record's literal has an invalid language-presence byte."),
        };

        EnsureAvailable(source, position, 1);
        byte direction = source[position++];
        TextDirection? baseDirection = direction switch
        {
            NoDirection => null,
            LtrDirection => TextDirection.Ltr,
            RtlDirection => TextDirection.Rtl,
            _ => throw new InvalidDataException("A term record's literal has an invalid base-direction byte."),
        };

        return new Literal(value, datatype, language, baseDirection);
    }

    /// <summary>Reads a length-prefixed field and interns its bytes into <paramref name="pool"/>, bounding the declared length against the bytes remaining before reading.</summary>
    /// <param name="source">The record bytes.</param>
    /// <param name="position">The read cursor; advanced past the length and content.</param>
    /// <param name="pool">The pool the content bytes are interned into.</param>
    /// <returns>The interned content.</returns>
    /// <exception cref="InvalidDataException">The length prefix or content runs past the source bounds.</exception>
    private static Utf8String ReadField(ReadOnlySpan<byte> source, ref int position, Utf8StringPool pool)
    {
        EnsureAvailable(source, position, LengthPrefixWidth);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
        position += LengthPrefixWidth;
        if(length > (uint)(source.Length - position))
        {
            throw new InvalidDataException("A term record field declares a length beyond the record bounds.");
        }

        Utf8String interned = pool.Intern(source.Slice(position, (int)length));
        position += (int)length;

        return interned;
    }

    /// <summary>Throws when a record is too short to hold the next <paramref name="needed"/> bytes.</summary>
    /// <param name="source">The record bytes.</param>
    /// <param name="position">The read cursor.</param>
    /// <param name="needed">The number of bytes required at the cursor.</param>
    /// <exception cref="InvalidDataException">Fewer than <paramref name="needed"/> bytes remain.</exception>
    private static void EnsureAvailable(ReadOnlySpan<byte> source, int position, int needed)
    {
        if(needed > source.Length - position)
        {
            throw new InvalidDataException("A term record is truncated.");
        }
    }

    /// <summary>Builds the exception for a term kind the codec does not write.</summary>
    /// <param name="term">The unsupported term.</param>
    /// <returns>The exception to throw.</returns>
    private static NotSupportedException UnsupportedTerm(RdfTerm term)
    {
        return new NotSupportedException($"The term record codec does not encode the term kind '{term.GetType().Name}'.");
    }

    /// <summary>A partially-built triple term during the iterative read: its component slots fill in subject, predicate, object order, and the frame builds the term once the third arrives. An explicit frame replaces the recursive descent a nested triple term would otherwise need.</summary>
    internal sealed class TripleFrame
    {
        /// <summary>The subject component, or <see langword="null"/> until filled.</summary>
        private RdfTerm? Subject { get; set; }

        /// <summary>The predicate component, or <see langword="null"/> until filled.</summary>
        private RdfTerm? Predicate { get; set; }

        /// <summary>The object component, or <see langword="null"/> until filled.</summary>
        private RdfTerm? Object { get; set; }

        /// <summary>The number of components filled so far.</summary>
        private int Filled { get; set; }

        /// <summary>Whether all three components have been filled.</summary>
        public bool IsComplete => Filled == 3;

        /// <summary>Fills the next component slot — subject, then predicate, then object.</summary>
        /// <param name="term">The component term.</param>
        public void Fill(RdfTerm term)
        {
            if(Filled == 0)
            {
                Subject = term;
            }
            else if(Filled == 1)
            {
                Predicate = term;
            }
            else
            {
                Object = term;
            }

            Filled++;
        }

        /// <summary>Builds the triple term from its filled components, validating that the predicate is a named node.</summary>
        /// <returns>The reconstructed triple term.</returns>
        /// <exception cref="InvalidDataException">The predicate component is not a named node.</exception>
        public TripleTerm Build()
        {
            if(Predicate is not NamedNode namedPredicate)
            {
                throw new InvalidDataException("A term record's triple-term predicate is not a named node.");
            }

            return new TripleTerm(Subject!, namedPredicate, Object!);
        }
    }
}
