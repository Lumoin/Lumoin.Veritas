using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Lexer;

/// <summary>
/// Tokenises UTF-8 Turtle and TriG input one token at a time.
/// </summary>
/// <remarks>
/// <para>
/// The lexer is iterative — no recursion — and yields tokens lazily
/// through <see cref="Tokenize"/>. State is local to the instance:
/// one lexer per document.
/// </para>
/// <para>
/// Byte access is through <see cref="SequenceReader{T}"/> over a
/// <see cref="ReadOnlySequence{T}"/>, so a token that straddles two
/// buffer segments is read without first gathering the whole source
/// into one contiguous block. A <see cref="ReadOnlyMemory{T}"/> source
/// is wrapped as a single-segment sequence.
/// </para>
/// <para>
/// Position tracking is byte-accurate. Line and column counts are
/// zero-based; columns advance one per byte rather than per Unicode
/// code point because the project's pipeline works in UTF-8 throughout
/// and editor surfaces convert on the boundary.
/// </para>
/// <para>
/// String interning is the caller's responsibility through the supplied
/// <see cref="Utf8StringPool"/>. The lexer interns every payload — IRI
/// bytes, prefixed-name bytes, blank-node labels, language tags, decoded
/// string-literal values — so the parser receives stable
/// <see cref="Utf8String"/> handles that compare and hash without
/// touching the underlying source memory.
/// </para>
/// </remarks>
public sealed class TurtleLexer
{
    private long consumed;
    private int line;
    private int column;

    /// <summary>
    /// Initialises a new <see cref="TurtleLexer"/> for the supplied UTF-8 source bytes.
    /// </summary>
    /// <param name="source">The UTF-8 encoded source document.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    public TurtleLexer(ReadOnlyMemory<byte> source, Utf8StringPool pool)
        : this(new ReadOnlySequence<byte>(source), pool)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TurtleLexer"/> for the supplied UTF-8 source sequence.
    /// </summary>
    /// <param name="source">The UTF-8 encoded source document, possibly spanning segments.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    public TurtleLexer(ReadOnlySequence<byte> source, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Source = source;
        Pool = pool;
    }

    /// <summary>Gets the UTF-8 source bytes being lexed.</summary>
    private ReadOnlySequence<byte> Source { get; }

    /// <summary>Gets the pool used to intern token payloads.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>
    /// Iterates the source document producing tokens until end of input.
    /// </summary>
    /// <remarks>
    /// The iterator yields an <see cref="TurtleTokenKind.EndOfInput"/>
    /// token as its final element so the parser can drive its loop
    /// without checking the source position separately.
    /// </remarks>
    /// <returns>An iterator over the source tokens.</returns>
    public IEnumerable<TurtleToken> Tokenize()
    {
        while(true)
        {
            TurtleToken token = LexNextToken();
            yield return token;

            if(token.Kind == TurtleTokenKind.EndOfInput)
            {
                yield break;
            }
        }
    }

    private TurtleToken LexNextToken()
    {
        //A SequenceReader is a ref struct and cannot live across a yield, so each token
        //positions a fresh reader at the running offset. For the single-segment whole-buffer
        //feed this advance is constant-time.
        SequenceReader<byte> reader = new(Source);
        reader.Advance(consumed);

        SkipWhitespaceAndComments(ref reader);

        TurtleToken token;
        if(reader.End)
        {
            int offset = (int)reader.Consumed;
            token = new TurtleToken(
                TurtleTokenKind.EndOfInput,
                CaptureSpan(offset, offset, line, column, line, column),
                Pool.Intern(ReadOnlySpan<byte>.Empty));
        }
        else
        {
            token = LexNext(ref reader);
        }

        consumed = reader.Consumed;

        return token;
    }

    private TurtleToken LexNext(ref SequenceReader<byte> reader)
    {
        reader.TryPeek(out byte current);

        return current switch
        {
            (byte)'<' => LexLessThan(ref reader),
            (byte)'>' => LexGreaterThan(ref reader),
            (byte)'"' => LexShortString(ref reader, (byte)'"'),
            (byte)'\'' => LexShortString(ref reader, (byte)'\''),
            (byte)'_' => LexBlankNodeLabel(ref reader),
            (byte)'[' => LexOpenBracket(ref reader),
            (byte)']' => LexSinglePunctuation(ref reader, TurtleTokenKind.CloseBracket),
            (byte)'(' => LexSinglePunctuation(ref reader, TurtleTokenKind.OpenParen),
            (byte)')' => LexCloseParen(ref reader),
            (byte)'{' => LexOpenBrace(ref reader),
            (byte)'}' => LexSinglePunctuation(ref reader, TurtleTokenKind.CloseBrace),
            (byte)'|' => LexPipe(ref reader),
            (byte)',' => LexSinglePunctuation(ref reader, TurtleTokenKind.Comma),
            (byte)';' => LexSinglePunctuation(ref reader, TurtleTokenKind.Semicolon),
            (byte)'.' => LexPeriodOrDecimal(ref reader),
            (byte)'~' => LexSinglePunctuation(ref reader, TurtleTokenKind.Tilde),
            (byte)'^' => LexTypeMarker(ref reader),
            (byte)'@' => LexAtDirective(ref reader),
            (byte)'+' or (byte)'-' => LexNumericSigned(ref reader),
            >= (byte)'0' and <= (byte)'9' => LexNumericUnsigned(ref reader),
            _ => LexIdentifierLike(ref reader)
        };
    }

    private TurtleToken LexLessThan(ref SequenceReader<byte> reader)
    {
        //Distinguish '<' (IRI), '<<' (reified triple), '<<(' (triple term).
        if(reader.TryPeek(1, out byte second) && second == (byte)'<' && reader.TryPeek(2, out byte third) && third == (byte)'(')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 3);

            return new TurtleToken(
                TurtleTokenKind.OpenTripleTerm,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("<<("u8));
        }

        if(reader.TryPeek(1, out byte angle) && angle == (byte)'<')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            return new TurtleToken(
                TurtleTokenKind.OpenReifiedTriple,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("<<"u8));
        }

        return LexIri(ref reader);
    }

    private TurtleToken LexGreaterThan(ref SequenceReader<byte> reader)
    {
        if(reader.TryPeek(1, out byte angle) && angle == (byte)'>')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            return new TurtleToken(
                TurtleTokenKind.CloseReifiedTriple,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern(">>"u8));
        }

        throw new TurtleParseException(
            $"Unexpected '>' at line {line + 1} column {column + 1}.",
            SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
    }

    private TurtleToken LexOpenBrace(ref SequenceReader<byte> reader)
    {
        if(reader.TryPeek(1, out byte bar) && bar == (byte)'|')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            return new TurtleToken(
                TurtleTokenKind.OpenAnnotation,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("{|"u8));
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.OpenBrace);
    }

    private TurtleToken LexPipe(ref SequenceReader<byte> reader)
    {
        if(reader.TryPeek(1, out byte brace) && brace == (byte)'}')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            return new TurtleToken(
                TurtleTokenKind.CloseAnnotation,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("|}"u8));
        }

        throw new TurtleParseException(
            $"Unexpected '|' at line {line + 1} column {column + 1}.",
            SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
    }

    private TurtleToken LexOpenBracket(ref SequenceReader<byte> reader)
    {
        //Detect the ANON form '[' WS* ']' so the parser can produce a single anonymous-blank-node term.
        long probeOffset = 1;
        while(reader.TryPeek(probeOffset, out byte whitespace) && IsWhitespaceByte(whitespace))
        {
            probeOffset++;
        }

        if(reader.TryPeek(probeOffset, out byte close) && close == (byte)']')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;

            long endConsumed = reader.Consumed + probeOffset + 1;
            while(reader.Consumed < endConsumed)
            {
                Advance(ref reader);
            }

            return new TurtleToken(
                TurtleTokenKind.AnonymousBlankNode,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("[]"u8));
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.OpenBracket);
    }

    private TurtleToken LexCloseParen(ref SequenceReader<byte> reader)
    {
        //RDF 1.2 triple-term close is the three-byte sequence ')>>'.
        if(reader.TryPeek(1, out byte first) && first == (byte)'>' && reader.TryPeek(2, out byte second) && second == (byte)'>')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 3);

            return new TurtleToken(
                TurtleTokenKind.CloseTripleTerm,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern(")>>"u8));
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.CloseParen);
    }

    private TurtleToken LexSinglePunctuation(ref SequenceReader<byte> reader, TurtleTokenKind kind)
    {
        int startByte = (int)reader.Consumed;
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        return new TurtleToken(
            kind,
            CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));
    }

    private TurtleToken LexPeriodOrDecimal(ref SequenceReader<byte> reader)
    {
        //A leading period followed by a digit begins a numeric literal: ".5", ".5e10".
        if(reader.TryPeek(1, out byte next) && IsDigit(next))
        {
            return LexNumericUnsigned(ref reader);
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.Period);
    }

    private TurtleToken LexTypeMarker(ref SequenceReader<byte> reader)
    {
        if(reader.TryPeek(1, out byte caret) && caret == (byte)'^')
        {
            int startByte = (int)reader.Consumed;
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            return new TurtleToken(
                TurtleTokenKind.TypeMarker,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("^^"u8));
        }

        throw new TurtleParseException(
            $"Expected '^^' at line {line + 1} column {column + 1}.",
            SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
    }

    private TurtleToken LexAtDirective(ref SequenceReader<byte> reader)
    {
        int startByte = (int)reader.Consumed;
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        SequencePosition identifierStartPosition = reader.Position;
        int identifierStartByte = (int)reader.Consumed;
        while(reader.TryPeek(out byte letter) && IsAsciiLetter(letter))
        {
            Advance(ref reader);
        }

        if((int)reader.Consumed == identifierStartByte)
        {
            throw new TurtleParseException(
                $"Expected identifier after '@' at line {startLine + 1} column {startColumn + 1}.",
                SourceSpan.SingleLine(startByte, (int)reader.Consumed, startLine, startColumn, column));
        }

        ReadOnlySequence<byte> identifier = reader.Sequence.Slice(identifierStartPosition, reader.Position);

        if(SequenceEquals(identifier, "prefix"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.PrefixKeyword,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("@prefix"u8));
        }

        if(SequenceEquals(identifier, "base"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.BaseKeyword,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("@base"u8));
        }

        if(SequenceEquals(identifier, "version"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.VersionKeyword,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("@version"u8));
        }

        //Otherwise this is a language tag attached to the preceding literal: continue reading subtags and an optional direction.
        return LexLanguageTagBody(ref reader, startByte, startLine, startColumn, identifierStartPosition);
    }

    private TurtleToken LexLanguageTagBody(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition identifierStartPosition)
    {
        bool sawDirection = false;

        while(reader.TryPeek(out byte dash) && dash == (byte)'-')
        {
            //RDF 1.2 direction marker '--' separates language subtags from the base direction.
            if(reader.TryPeek(1, out byte secondDash) && secondDash == (byte)'-')
            {
                AdvanceCount(ref reader, 2);
                sawDirection = true;
                int dirStart = (int)reader.Consumed;

                while(reader.TryPeek(out byte letter) && IsAsciiLetter(letter))
                {
                    Advance(ref reader);
                }

                if((int)reader.Consumed == dirStart)
                {
                    throw new TurtleParseException(
                        $"Expected direction tag after '--' at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
                }

                break;
            }

            Advance(ref reader);
            int subtagStart = (int)reader.Consumed;
            while(reader.TryPeek(out byte alnum) && IsAsciiLetterOrDigit(alnum))
            {
                Advance(ref reader);
            }

            if((int)reader.Consumed == subtagStart)
            {
                throw new TurtleParseException(
                    $"Expected language subtag after '-' at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
            }
        }

        Utf8String value = Pool.Intern(reader.Sequence.Slice(identifierStartPosition, reader.Position));
        TurtleTokenKind kind = sawDirection ? TurtleTokenKind.DirLangTag : TurtleTokenKind.LangTag;

        return new TurtleToken(
            kind,
            CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
            value);
    }

    private TurtleToken LexIri(ref SequenceReader<byte> reader)
    {
        int startByte = (int)reader.Consumed;
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        byte[] rented = ArrayPool<byte>.Shared.Rent(64);
        int written = 0;

        try
        {
            while(true)
            {
                if(!reader.TryPeek(out byte b))
                {
                    throw new TurtleParseException(
                        $"Unterminated IRI starting at line {startLine + 1} column {startColumn + 1}.",
                        SourceSpan.SingleLine(startByte, (int)reader.Consumed, startLine, startColumn, column));
                }

                if(b == (byte)'>')
                {
                    Advance(ref reader);

                    Utf8String iriValue = Pool.Intern(rented.AsSpan(0, written));

                    return new TurtleToken(
                        TurtleTokenKind.Iri,
                        CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                        iriValue);
                }

                if(b == (byte)'\\')
                {
                    int escapeStart = (int)reader.Consumed;
                    int escapeLine = line;
                    int escapeColumn = column;

                    uint decoded = ReadUcharEscape(ref reader, escapeStart, escapeLine, escapeColumn);
                    written = AppendCodepoint(decoded, ref rented, written);
                    continue;
                }

                if(b < 0x21 || b == (byte)'"' || b == (byte)'<' || b == (byte)'{' || b == (byte)'}' || b == (byte)'|' || b == (byte)'^' || b == (byte)'`')
                {
                    throw new TurtleParseException(
                        $"Invalid byte 0x{b:X2} inside IRI at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
                }

                int byteCount = Utf8ByteLength(b);
                EnsureCapacity(ref rented, written + byteCount);

                if(reader.Remaining < byteCount)
                {
                    throw new TurtleParseException(
                        $"Truncated UTF-8 sequence inside IRI at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Length, line, column, column));
                }

                for(int i = 0; i < byteCount; i++)
                {
                    reader.TryPeek(out byte continuation);
                    rented[written++] = continuation;
                    Advance(ref reader);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private uint ReadUcharEscape(ref SequenceReader<byte> reader, int escapeStart, int escapeLine, int escapeColumn)
    {
        //Decodes a \uXXXX or \UXXXXXXXX escape to a full Unicode scalar value. Each escape stands
        //for one scalar value: surrogate code points (U+D800..U+DFFF) are rejected — Turtle does
        //not encode astral characters as \u surrogate pairs, only as a single \U escape — and code
        //points beyond U+10FFFF are rejected as out of range.
        if(!reader.TryPeek(1, out byte marker))
        {
            throw new TurtleParseException(
                $"Truncated escape sequence at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, (int)reader.Consumed, escapeLine, escapeColumn, column));
        }

        int hexCount = marker switch
        {
            (byte)'u' => 4,
            (byte)'U' => 8,
            _ => -1
        };

        if(hexCount < 0)
        {
            throw new TurtleParseException(
                $"Invalid escape '\\{(char)marker}' inside IRI at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, (int)reader.Consumed + 2, escapeLine, escapeColumn, column + 2));
        }

        if(reader.Remaining < 2 + hexCount)
        {
            throw new TurtleParseException(
                $"Truncated \\{(char)marker} escape at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, (int)reader.Length, escapeLine, escapeColumn, column));
        }

        AdvanceCount(ref reader, 2);

        uint codepoint = 0;
        for(int i = 0; i < hexCount; i++)
        {
            reader.TryPeek(out byte hex);
            if(!TryHexValue(hex, out uint digit))
            {
                throw new TurtleParseException(
                    $"Invalid hex digit '{(char)hex}' in escape at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
            }

            codepoint = (codepoint << 4) | digit;
            Advance(ref reader);
        }

        if(codepoint >= (uint)UnicodeConstants.SurrogateRangeFirst && codepoint <= (uint)UnicodeConstants.SurrogateRangeLast)
        {
            throw new TurtleParseException(
                $"Surrogate code point U+{codepoint:X4} is not a valid Unicode scalar value at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, (int)reader.Consumed, escapeLine, escapeColumn, column));
        }

        if(codepoint > (uint)UnicodeConstants.MaximumCodePoint)
        {
            throw new TurtleParseException(
                $"Code point U+{codepoint:X} exceeds the Unicode range at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, (int)reader.Consumed, escapeLine, escapeColumn, column));
        }

        return codepoint;
    }

    private TurtleToken LexShortString(ref SequenceReader<byte> reader, byte quote)
    {
        int startByte = (int)reader.Consumed;
        int startLine = line;
        int startColumn = column;

        //Detect the three-quote long-string form.
        if(reader.TryPeek(1, out byte secondQuote) && secondQuote == quote && reader.TryPeek(2, out byte thirdQuote) && thirdQuote == quote)
        {
            return LexLongString(ref reader, quote);
        }

        Advance(ref reader);

        byte[] rented = ArrayPool<byte>.Shared.Rent(64);
        int written = 0;

        try
        {
            while(true)
            {
                if(!reader.TryPeek(out byte b))
                {
                    throw new TurtleParseException(
                        $"Unterminated string literal starting at line {startLine + 1} column {startColumn + 1}.",
                        SourceSpan.SingleLine(startByte, (int)reader.Consumed, startLine, startColumn, column));
                }

                if(b == quote)
                {
                    Advance(ref reader);
                    Utf8String value = Pool.Intern(rented.AsSpan(0, written));

                    return new TurtleToken(
                        TurtleTokenKind.StringLiteral,
                        CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                        value);
                }

                if(b == (byte)'\n' || b == (byte)'\r')
                {
                    throw new TurtleParseException(
                        $"Unescaped line break inside short string literal at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
                }

                if(b == (byte)'\\')
                {
                    written = DecodeEscape(ref reader, ref rented, written);
                    continue;
                }

                int byteCount = Utf8ByteLength(b);
                if(reader.Remaining < byteCount)
                {
                    throw new TurtleParseException(
                        $"Truncated UTF-8 sequence inside string literal at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Length, line, column, column));
                }

                EnsureCapacity(ref rented, written + byteCount);

                for(int i = 0; i < byteCount; i++)
                {
                    reader.TryPeek(out byte continuation);
                    rented[written++] = continuation;
                    Advance(ref reader);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private TurtleToken LexLongString(ref SequenceReader<byte> reader, byte quote)
    {
        int startByte = (int)reader.Consumed;
        int startLine = line;
        int startColumn = column;

        AdvanceCount(ref reader, 3);

        byte[] rented = ArrayPool<byte>.Shared.Rent(128);
        int written = 0;

        try
        {
            while(true)
            {
                if(!reader.TryPeek(out byte b))
                {
                    throw new TurtleParseException(
                        $"Unterminated long string literal starting at line {startLine + 1} column {startColumn + 1}.",
                        SourceSpan.SingleLine(startByte, (int)reader.Consumed, startLine, startColumn, column));
                }

                if(b == quote && reader.TryPeek(1, out byte secondQuote) && secondQuote == quote && reader.TryPeek(2, out byte thirdQuote) && thirdQuote == quote)
                {
                    AdvanceCount(ref reader, 3);
                    Utf8String value = Pool.Intern(rented.AsSpan(0, written));

                    return new TurtleToken(
                        TurtleTokenKind.LongStringLiteral,
                        CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                        value);
                }

                if(b == (byte)'\\')
                {
                    written = DecodeEscape(ref reader, ref rented, written);
                    continue;
                }

                int byteCount = Utf8ByteLength(b);
                if(reader.Remaining < byteCount)
                {
                    throw new TurtleParseException(
                        $"Truncated UTF-8 sequence inside long string literal at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Length, line, column, column));
                }

                EnsureCapacity(ref rented, written + byteCount);

                for(int i = 0; i < byteCount; i++)
                {
                    reader.TryPeek(out byte continuation);
                    rented[written++] = continuation;
                    Advance(ref reader);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private int DecodeEscape(ref SequenceReader<byte> reader, ref byte[] rented, int written)
    {
        if(!reader.TryPeek(1, out byte marker))
        {
            throw new TurtleParseException(
                $"Truncated escape sequence at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Length, line, column, column));
        }

        switch(marker)
        {
            case (byte)'t':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\t';
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'b':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = 0x08;
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'n':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\n';
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'r':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\r';
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'f':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = 0x0C;
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'"':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'"';
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'\'':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\'';
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'\\':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\\';
                AdvanceCount(ref reader, 2);

                return written;
            }

            case (byte)'u':
            case (byte)'U':
            {
                int escapeStart = (int)reader.Consumed;
                int escapeLine = line;
                int escapeColumn = column;
                uint decoded = ReadUcharEscape(ref reader, escapeStart, escapeLine, escapeColumn);

                return AppendCodepoint(decoded, ref rented, written);
            }

            default:
            {
                throw new TurtleParseException(
                    $"Invalid escape sequence '\\{(char)marker}' at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 2, line, column, column + 2));
            }
        }
    }

    private static int AppendCodepoint(uint codepoint, ref byte[] rented, int written)
    {
        Span<char> chars = stackalloc char[2];
        int charCount = new System.Text.Rune((int)codepoint).EncodeToUtf16(chars);
        int maxBytes = Encoding.UTF8.GetMaxByteCount(charCount);
        EnsureCapacity(ref rented, written + maxBytes);
        int encoded = Encoding.UTF8.GetBytes(chars[..charCount], rented.AsSpan(written));

        return written + encoded;
    }

    private TurtleToken LexBlankNodeLabel(ref SequenceReader<byte> reader)
    {
        if(!reader.TryPeek(1, out byte colon) || colon != (byte)':')
        {
            throw new TurtleParseException(
                $"Expected ':' after '_' at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
        }

        int startByte = (int)reader.Consumed;
        int startLine = line;
        int startColumn = column;

        AdvanceCount(ref reader, 2);

        SequencePosition labelStartPosition = reader.Position;
        if(!reader.TryPeek(out byte first) || !IsPnCharsUOrDigit(first))
        {
            throw new TurtleParseException(
                $"Expected blank-node label after '_:' at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
        }

        Advance(ref reader);

        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;

        //Greedy scan; a trailing '.' that is part of the statement terminator is stripped after the loop.
        while(reader.TryPeek(out byte b) && (IsPnChars(b) || b == (byte)'.'))
        {
            Advance(ref reader);

            if(b != (byte)'.')
            {
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
            }
        }

        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        Utf8String label = Pool.Intern(reader.Sequence.Slice(labelStartPosition, reader.Position));

        return new TurtleToken(
            TurtleTokenKind.BlankNodeLabel,
            CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
            label);
    }

    private TurtleToken LexIdentifierLike(ref SequenceReader<byte> reader)
    {
        //Identifier-like tokens include: prefixed names (with or without an explicit prefix),
        //the rdf:type shorthand 'a', the boolean keywords, the SPARQL-style PREFIX / BASE / VERSION
        //keywords, and TriG's GRAPH keyword.
        int startByte = (int)reader.Consumed;
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;
        reader.TryPeek(out byte first);

        if(first == (byte)':')
        {
            return LexPrefixedNameStartingWithColon(ref reader, startByte, startLine, startColumn, startPosition);
        }

        if(!IsPnCharsBase(first))
        {
            throw new TurtleParseException(
                $"Unexpected byte 0x{first:X2} at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
        }

        //Scan a PN_PREFIX-like identifier.
        Advance(ref reader);

        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;

        while(reader.TryPeek(out byte b) && (IsPnChars(b) || b == (byte)'.'))
        {
            Advance(ref reader);

            if(b != (byte)'.')
            {
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
            }
        }

        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        ReadOnlySequence<byte> identifierSpan = reader.Sequence.Slice(startPosition, reader.Position);

        //Distinguish a prefix declaration vs. a prefixed-name vs. a keyword by what follows.
        if(reader.TryPeek(out byte separator) && separator == (byte)':')
        {
            Advance(ref reader);

            return LexPrefixedNameOrNamespace(ref reader, startByte, startLine, startColumn, startPosition);
        }

        if(SequenceEquals(identifierSpan, "a"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.A,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("a"u8));
        }

        if(SequenceEquals(identifierSpan, "true"u8) || SequenceEquals(identifierSpan, "false"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.BooleanLiteral,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern(identifierSpan));
        }

        if(SequenceEqualsIgnoreAsciiCase(identifierSpan, "PREFIX"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.PrefixKeyword,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("PREFIX"u8));
        }

        if(SequenceEqualsIgnoreAsciiCase(identifierSpan, "BASE"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.BaseKeyword,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("BASE"u8));
        }

        if(SequenceEqualsIgnoreAsciiCase(identifierSpan, "VERSION"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.VersionKeyword,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("VERSION"u8));
        }

        if(SequenceEquals(identifierSpan, "GRAPH"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.GraphKeyword,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern("GRAPH"u8));
        }

        throw new TurtleParseException(
            $"Unrecognised identifier '{DecodeForMessage(identifierSpan)}' at line {startLine + 1} column {startColumn + 1}.",
            CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column));
    }

    private TurtleToken LexPrefixedNameStartingWithColon(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition startPosition)
    {
        Advance(ref reader);

        return LexPrefixedNameOrNamespace(ref reader, startByte, startLine, startColumn, startPosition);
    }

    private TurtleToken LexPrefixedNameOrNamespace(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition startPosition)
    {
        long localStartConsumed = reader.Consumed;
        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;
        bool firstByte = true;

        while(reader.TryPeek(out byte b))
        {
            bool accept;

            if(firstByte)
            {
                accept = IsPnCharsUOrDigit(b) || b == (byte)':' || b == (byte)'%' || b == (byte)'\\';
            }
            else
            {
                accept = IsPnChars(b) || b == (byte)':' || b == (byte)'%' || b == (byte)'\\' || b == (byte)'.';
            }

            if(!accept)
            {
                break;
            }

            //Handle reserved-character escapes used inside PN_LOCAL.
            if(b == (byte)'\\')
            {
                if(!reader.TryPeek(1, out _))
                {
                    throw new TurtleParseException(
                        $"Truncated escape inside prefixed name at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 1, line, column, column + 1));
                }

                AdvanceCount(ref reader, 2);
                firstByte = false;
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
                continue;
            }

            if(b == (byte)'%')
            {
                //Percent-encoded byte: '%' HEX HEX.
                if(!reader.TryPeek(1, out byte highHex)
                   || !reader.TryPeek(2, out byte lowHex)
                   || !TryHexValue(highHex, out _)
                   || !TryHexValue(lowHex, out _))
                {
                    throw new TurtleParseException(
                        $"Malformed percent escape inside prefixed name at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine((int)reader.Consumed, (int)reader.Consumed + 3, line, column, column + 3));
                }

                AdvanceCount(ref reader, 3);
                firstByte = false;
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
                continue;
            }

            Advance(ref reader);
            firstByte = false;

            if(b != (byte)'.')
            {
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
            }
        }

        //A trailing '.' is part of the statement terminator, not the local name.
        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        ReadOnlySequence<byte> fullSpan = reader.Sequence.Slice(startPosition, reader.Position);

        //If nothing followed the colon, this is a PNAME_NS (prefix declaration form).
        if(reader.Consumed == localStartConsumed)
        {
            return new TurtleToken(
                TurtleTokenKind.PrefixNamespace,
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
                Pool.Intern(fullSpan));
        }

        return new TurtleToken(
            TurtleTokenKind.PrefixedName,
            CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
            Pool.Intern(fullSpan));
    }

    private TurtleToken LexNumericSigned(ref SequenceReader<byte> reader)
    {
        int startByte = (int)reader.Consumed;
        int startLine = line;
        int startColumn = column;

        reader.TryPeek(out byte sign);
        Advance(ref reader);

        if(!reader.TryPeek(out byte after) || (!IsDigit(after) && after != (byte)'.'))
        {
            throw new TurtleParseException(
                $"Expected digit after '{(char)sign}' at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine(startByte, (int)reader.Consumed, startLine, startColumn, column));
        }

        return ContinueNumeric(ref reader, startByte, startLine, startColumn);
    }

    private TurtleToken LexNumericUnsigned(ref SequenceReader<byte> reader)
    {
        int startByte = (int)reader.Consumed;
        int startLine = line;
        int startColumn = column;

        return ContinueNumeric(ref reader, startByte, startLine, startColumn);
    }

    private TurtleToken ContinueNumeric(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn)
    {
        SequencePosition startPosition = reader.Sequence.GetPosition(startByte);
        bool sawDecimal = false;
        bool sawExponent = false;
        bool sawDigit = false;

        while(reader.TryPeek(out byte digit) && IsDigit(digit))
        {
            sawDigit = true;
            Advance(ref reader);
        }

        if(reader.TryPeek(out byte dot) && dot == (byte)'.')
        {
            //A '.' that does not precede a digit belongs to the statement terminator.
            if(reader.TryPeek(1, out byte afterDot) && IsDigit(afterDot))
            {
                sawDecimal = true;
                Advance(ref reader);

                while(reader.TryPeek(out byte fraction) && IsDigit(fraction))
                {
                    sawDigit = true;
                    Advance(ref reader);
                }
            }
        }

        if(reader.TryPeek(out byte exponent) && (exponent == (byte)'e' || exponent == (byte)'E'))
        {
            sawExponent = true;
            Advance(ref reader);

            if(reader.TryPeek(out byte expSign) && (expSign == (byte)'+' || expSign == (byte)'-'))
            {
                Advance(ref reader);
            }

            int expStart = (int)reader.Consumed;
            while(reader.TryPeek(out byte expDigit) && IsDigit(expDigit))
            {
                Advance(ref reader);
            }

            if((int)reader.Consumed == expStart)
            {
                throw new TurtleParseException(
                    $"Expected exponent digits at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine(startByte, (int)reader.Consumed, startLine, startColumn, column));
            }
        }

        if(!sawDigit)
        {
            throw new TurtleParseException(
                $"Invalid numeric literal at line {startLine + 1} column {startColumn + 1}.",
                CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column));
        }

        TurtleTokenKind kind = (sawExponent, sawDecimal) switch
        {
            (true, _) => TurtleTokenKind.DoubleLiteral,
            (false, true) => TurtleTokenKind.DecimalLiteral,
            (false, false) => TurtleTokenKind.IntegerLiteral
        };

        Utf8String value = Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position));

        return new TurtleToken(
            kind,
            CaptureSpan(startByte, (int)reader.Consumed, startLine, startColumn, line, column),
            value);
    }

    private void SkipWhitespaceAndComments(ref SequenceReader<byte> reader)
    {
        while(reader.TryPeek(out byte b))
        {
            if(IsWhitespaceByte(b))
            {
                Advance(ref reader);
                continue;
            }

            if(b == (byte)'#')
            {
                while(reader.TryPeek(out byte commentByte) && commentByte != (byte)'\n' && commentByte != (byte)'\r')
                {
                    Advance(ref reader);
                }

                continue;
            }

            break;
        }
    }

    private void Advance(ref SequenceReader<byte> reader)
    {
        reader.TryRead(out byte b);

        if(b == (byte)'\n')
        {
            line++;
            column = 0;
        }
        else if(b == (byte)'\r')
        {
            //CR is treated as a line break by itself; a CR LF sequence emits a single newline.
            line++;
            column = 0;

            if(reader.TryPeek(out byte next) && next == (byte)'\n')
            {
                reader.Advance(1);
            }
        }
        else
        {
            column++;
        }
    }

    private void AdvanceCount(ref SequenceReader<byte> reader, int count)
    {
        for(int i = 0; i < count; i++)
        {
            Advance(ref reader);
        }
    }

    private void StripToLastSignificant(ref SequenceReader<byte> reader, long lastSignificantConsumed, int lastSignificantColumn)
    {
        //A run of trailing '.' bytes belongs to the statement terminator, not the token; rewind to
        //the byte after the last non-'.' the scan accepted.
        long rewind = reader.Consumed - lastSignificantConsumed;
        if(rewind > 0)
        {
            reader.Rewind(rewind);
            column = lastSignificantColumn;
        }
    }

    private static SourceSpan CaptureSpan(int startByte, int endByte, int startLine, int startColumn, int endLine, int endColumn)
    {
        return new SourceSpan(startByte, endByte, startLine, startColumn, endLine, endColumn);
    }

    private static bool IsWhitespaceByte(byte b)
    {
        return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
    }

    private static bool IsDigit(byte b)
    {
        return b >= (byte)'0' && b <= (byte)'9';
    }

    private static bool IsAsciiLetter(byte b)
    {
        return (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z');
    }

    private static bool IsAsciiLetterOrDigit(byte b)
    {
        return IsAsciiLetter(b) || IsDigit(b);
    }

    private static bool IsPnCharsBase(byte b)
    {
        //ASCII subset of PN_CHARS_BASE plus the lead byte of any non-ASCII UTF-8 sequence.
        //Full Unicode validation is deferred to consumers that need exact range checks.
        return IsAsciiLetter(b) || b >= 0x80;
    }

    private static bool IsPnCharsUOrDigit(byte b)
    {
        return IsPnCharsBase(b) || b == (byte)'_' || IsDigit(b);
    }

    private static bool IsPnChars(byte b)
    {
        return IsPnCharsUOrDigit(b) || b == (byte)'-' || b == 0xB7;
    }

    private static int Utf8ByteLength(byte leadByte)
    {
        if(leadByte < 0x80)
        {
            return 1;
        }

        if((leadByte & 0xE0) == 0xC0)
        {
            return 2;
        }

        if((leadByte & 0xF0) == 0xE0)
        {
            return 3;
        }

        if((leadByte & 0xF8) == 0xF0)
        {
            return 4;
        }

        throw new TurtleParseException(
            $"Invalid UTF-8 lead byte 0x{leadByte:X2}.");
    }

    private static bool TryHexValue(byte b, out uint value)
    {
        if(b >= (byte)'0' && b <= (byte)'9')
        {
            value = (uint)(b - (byte)'0');

            return true;
        }

        if(b >= (byte)'a' && b <= (byte)'f')
        {
            value = (uint)(b - (byte)'a' + 10);

            return true;
        }

        if(b >= (byte)'A' && b <= (byte)'F')
        {
            value = (uint)(b - (byte)'A' + 10);

            return true;
        }

        value = 0;

        return false;
    }

    private static byte ToAsciiLower(byte b)
    {
        return b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b + ('a' - 'A')) : b;
    }

    private static bool SequenceEquals(in ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> value)
    {
        if(sequence.Length != value.Length)
        {
            return false;
        }

        if(sequence.IsSingleSegment)
        {
            return sequence.FirstSpan.SequenceEqual(value);
        }

        int offset = 0;
        SequencePosition position = sequence.Start;
        while(sequence.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            ReadOnlySpan<byte> segment = memory.Span;
            if(!segment.SequenceEqual(value.Slice(offset, segment.Length)))
            {
                return false;
            }

            offset += segment.Length;
        }

        return true;
    }

    private static bool SequenceEqualsIgnoreAsciiCase(in ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> value)
    {
        if(sequence.Length != value.Length)
        {
            return false;
        }

        int offset = 0;
        SequencePosition position = sequence.Start;
        while(sequence.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            ReadOnlySpan<byte> segment = memory.Span;
            for(int i = 0; i < segment.Length; i++)
            {
                if(ToAsciiLower(segment[i]) != ToAsciiLower(value[offset + i]))
                {
                    return false;
                }
            }

            offset += segment.Length;
        }

        return true;
    }

    private static string DecodeForMessage(in ReadOnlySequence<byte> sequence)
    {
        return sequence.IsSingleSegment
            ? Encoding.UTF8.GetString(sequence.FirstSpan)
            : Encoding.UTF8.GetString(sequence.ToArray());
    }

    private static void EnsureCapacity(ref byte[] rented, int required)
    {
        if(required <= rented.Length)
        {
            return;
        }

        int newSize = Math.Max(required, rented.Length * 2);
        byte[] replacement = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(rented, replacement, rented.Length);
        ArrayPool<byte>.Shared.Return(rented);
        rented = replacement;
    }

    /// <summary>
    /// Gets the current zero-based byte position of the lexer. Exposed
    /// for diagnostic and test code; the public iteration surface is
    /// <see cref="Tokenize"/>.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal int Position => (int)consumed;
}
