using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    private int position;
    private int line;
    private int column;

    /// <summary>
    /// Initialises a new <see cref="TurtleLexer"/> for the supplied UTF-8 source bytes.
    /// </summary>
    /// <param name="source">The UTF-8 encoded source document.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    public TurtleLexer(ReadOnlyMemory<byte> source, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        SourceMemory = source;
        Pool = pool;
    }

    /// <summary>Gets the UTF-8 source bytes being lexed.</summary>
    private ReadOnlyMemory<byte> SourceMemory { get; }

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
            SkipWhitespaceAndComments();

            if(position >= SourceMemory.Length)
            {
                yield return new TurtleToken(
                    TurtleTokenKind.EndOfInput,
                    CaptureSpan(position, position, line, column, line, column),
                    Pool.Intern(ReadOnlySpan<byte>.Empty));

                yield break;
            }

            yield return LexNext();
        }
    }

    private TurtleToken LexNext()
    {
        ReadOnlySpan<byte> source = SourceMemory.Span;
        byte current = source[position];

        return current switch
        {
            (byte)'<' => LexLessThan(source),
            (byte)'>' => LexGreaterThan(source),
            (byte)'"' => LexShortString(source, (byte)'"'),
            (byte)'\'' => LexShortString(source, (byte)'\''),
            (byte)'_' => LexBlankNodeLabel(source),
            (byte)'[' => LexOpenBracket(source),
            (byte)']' => LexSinglePunctuation(TurtleTokenKind.CloseBracket),
            (byte)'(' => LexSinglePunctuation(TurtleTokenKind.OpenParen),
            (byte)')' => LexCloseParen(source),
            (byte)'{' => LexOpenBrace(source),
            (byte)'}' => LexSinglePunctuation(TurtleTokenKind.CloseBrace),
            (byte)'|' => LexPipe(source),
            (byte)',' => LexSinglePunctuation(TurtleTokenKind.Comma),
            (byte)';' => LexSinglePunctuation(TurtleTokenKind.Semicolon),
            (byte)'.' => LexPeriodOrDecimal(source),
            (byte)'~' => LexSinglePunctuation(TurtleTokenKind.Tilde),
            (byte)'^' => LexTypeMarker(source),
            (byte)'@' => LexAtDirective(source),
            (byte)'+' or (byte)'-' => LexNumericSigned(source),
            >= (byte)'0' and <= (byte)'9' => LexNumericUnsigned(source),
            _ => LexIdentifierLike(source)
        };
    }

    private TurtleToken LexLessThan(ReadOnlySpan<byte> source)
    {
        //Distinguish '<' (IRI), '<<' (reified triple), '<<(' (triple term).
        if(position + 2 < source.Length && source[position + 1] == (byte)'<' && source[position + 2] == (byte)'(')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;
            AdvanceBytes(3);

            return new TurtleToken(
                TurtleTokenKind.OpenTripleTerm,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("<<("u8));
        }

        if(position + 1 < source.Length && source[position + 1] == (byte)'<')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;
            AdvanceBytes(2);

            return new TurtleToken(
                TurtleTokenKind.OpenReifiedTriple,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("<<"u8));
        }

        return LexIri(source);
    }

    private TurtleToken LexGreaterThan(ReadOnlySpan<byte> source)
    {
        if(position + 1 < source.Length && source[position + 1] == (byte)'>')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;
            AdvanceBytes(2);

            return new TurtleToken(
                TurtleTokenKind.CloseReifiedTriple,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern(">>"u8));
        }

        throw new TurtleParseException(
            $"Unexpected '>' at line {line + 1} column {column + 1}.",
            SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
    }

    private TurtleToken LexOpenBrace(ReadOnlySpan<byte> source)
    {
        if(position + 1 < source.Length && source[position + 1] == (byte)'|')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;
            AdvanceBytes(2);

            return new TurtleToken(
                TurtleTokenKind.OpenAnnotation,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("{|"u8));
        }

        return LexSinglePunctuation(TurtleTokenKind.OpenBrace);
    }

    private TurtleToken LexPipe(ReadOnlySpan<byte> source)
    {
        if(position + 1 < source.Length && source[position + 1] == (byte)'}')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;
            AdvanceBytes(2);

            return new TurtleToken(
                TurtleTokenKind.CloseAnnotation,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("|}"u8));
        }

        throw new TurtleParseException(
            $"Unexpected '|' at line {line + 1} column {column + 1}.",
            SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
    }

    private TurtleToken LexOpenBracket(ReadOnlySpan<byte> source)
    {
        //Detect the ANON form '[' WS* ']' so the parser can produce a single anonymous-blank-node term.
        int probe = position + 1;
        while(probe < source.Length && IsWhitespaceByte(source[probe]))
        {
            probe++;
        }

        if(probe < source.Length && source[probe] == (byte)']')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;

            while(position <= probe)
            {
                AdvanceByte(source[position]);
            }

            return new TurtleToken(
                TurtleTokenKind.AnonymousBlankNode,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("[]"u8));
        }

        return LexSinglePunctuation(TurtleTokenKind.OpenBracket);
    }

    private TurtleToken LexCloseParen(ReadOnlySpan<byte> source)
    {
        //RDF 1.2 triple-term close is the three-byte sequence ')>>'.
        if(position + 2 < source.Length && source[position + 1] == (byte)'>' && source[position + 2] == (byte)'>')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;
            AdvanceBytes(3);

            return new TurtleToken(
                TurtleTokenKind.CloseTripleTerm,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern(")>>"u8));
        }

        return LexSinglePunctuation(TurtleTokenKind.CloseParen);
    }

    private TurtleToken LexSinglePunctuation(TurtleTokenKind kind)
    {
        int startByte = position;
        int startLine = line;
        int startColumn = column;

        AdvanceByte(SourceMemory.Span[position]);

        return new TurtleToken(
            kind,
            CaptureSpan(startByte, position, startLine, startColumn, line, column),
            Pool.Intern(SourceMemory.Span.Slice(startByte, position - startByte)));
    }

    private TurtleToken LexPeriodOrDecimal(ReadOnlySpan<byte> source)
    {
        //A leading period followed by a digit begins a numeric literal: ".5", ".5e10".
        if(position + 1 < source.Length && IsDigit(source[position + 1]))
        {
            return LexNumericUnsigned(source);
        }

        return LexSinglePunctuation(TurtleTokenKind.Period);
    }

    private TurtleToken LexTypeMarker(ReadOnlySpan<byte> source)
    {
        if(position + 1 < source.Length && source[position + 1] == (byte)'^')
        {
            int startByte = position;
            int startLine = line;
            int startColumn = column;
            AdvanceBytes(2);

            return new TurtleToken(
                TurtleTokenKind.TypeMarker,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("^^"u8));
        }

        throw new TurtleParseException(
            $"Expected '^^' at line {line + 1} column {column + 1}.",
            SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
    }

    private TurtleToken LexAtDirective(ReadOnlySpan<byte> source)
    {
        int startByte = position;
        int startLine = line;
        int startColumn = column;

        AdvanceByte((byte)'@');

        int identifierStart = position;
        while(position < source.Length && IsAsciiLetter(source[position]))
        {
            AdvanceByte(source[position]);
        }

        if(position == identifierStart)
        {
            throw new TurtleParseException(
                $"Expected identifier after '@' at line {startLine + 1} column {startColumn + 1}.",
                SourceSpan.SingleLine(startByte, position, startLine, startColumn, column));
        }

        ReadOnlySpan<byte> identifier = source.Slice(identifierStart, position - identifierStart);

        if(identifier.SequenceEqual("prefix"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.PrefixKeyword,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("@prefix"u8));
        }

        if(identifier.SequenceEqual("base"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.BaseKeyword,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("@base"u8));
        }

        if(identifier.SequenceEqual("version"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.VersionKeyword,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("@version"u8));
        }

        //Otherwise this is a language tag attached to the preceding literal: continue reading subtags and an optional direction.
        return LexLanguageTagBody(source, startByte, startLine, startColumn, identifierStart);
    }

    private TurtleToken LexLanguageTagBody(
        ReadOnlySpan<byte> source,
        int startByte,
        int startLine,
        int startColumn,
        int identifierStart)
    {
        bool sawDirection = false;

        while(position < source.Length && source[position] == (byte)'-')
        {
            //RDF 1.2 direction marker '--' separates language subtags from the base direction.
            if(position + 1 < source.Length && source[position + 1] == (byte)'-')
            {
                AdvanceBytes(2);
                sawDirection = true;
                int dirStart = position;

                while(position < source.Length && IsAsciiLetter(source[position]))
                {
                    AdvanceByte(source[position]);
                }

                if(position == dirStart)
                {
                    throw new TurtleParseException(
                        $"Expected direction tag after '--' at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
                }

                break;
            }

            AdvanceByte((byte)'-');
            int subtagStart = position;
            while(position < source.Length && IsAsciiLetterOrDigit(source[position]))
            {
                AdvanceByte(source[position]);
            }

            if(position == subtagStart)
            {
                throw new TurtleParseException(
                    $"Expected language subtag after '-' at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
            }
        }

        Utf8String value = Pool.Intern(source.Slice(identifierStart, position - identifierStart));
        TurtleTokenKind kind = sawDirection ? TurtleTokenKind.DirLangTag : TurtleTokenKind.LangTag;

        return new TurtleToken(
            kind,
            CaptureSpan(startByte, position, startLine, startColumn, line, column),
            value);
    }

    private TurtleToken LexIri(ReadOnlySpan<byte> source)
    {
        int startByte = position;
        int startLine = line;
        int startColumn = column;

        AdvanceByte((byte)'<');

        byte[] rented = ArrayPool<byte>.Shared.Rent(64);
        int written = 0;

        try
        {
            while(true)
            {
                if(position >= source.Length)
                {
                    throw new TurtleParseException(
                        $"Unterminated IRI starting at line {startLine + 1} column {startColumn + 1}.",
                        SourceSpan.SingleLine(startByte, position, startLine, startColumn, column));
                }

                byte b = source[position];
                if(b == (byte)'>')
                {
                    AdvanceByte((byte)'>');

                    Utf8String iriValue = Pool.Intern(rented.AsSpan(0, written));

                    return new TurtleToken(
                        TurtleTokenKind.Iri,
                        CaptureSpan(startByte, position, startLine, startColumn, line, column),
                        iriValue);
                }

                if(b == (byte)'\\')
                {
                    int escapeStart = position;
                    int escapeLine = line;
                    int escapeColumn = column;

                    uint decoded = ReadUcharEscape(source, escapeStart, escapeLine, escapeColumn);
                    written = AppendCodepoint(decoded, ref rented, written);
                    continue;
                }

                if(b < 0x21 || b == (byte)'"' || b == (byte)'<' || b == (byte)'{' || b == (byte)'}' || b == (byte)'|' || b == (byte)'^' || b == (byte)'`')
                {
                    throw new TurtleParseException(
                        $"Invalid byte 0x{b:X2} inside IRI at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
                }

                int byteCount = Utf8ByteLength(b);
                EnsureCapacity(ref rented, written + byteCount);
                int copyEnd = position + byteCount;

                if(copyEnd > source.Length)
                {
                    throw new TurtleParseException(
                        $"Truncated UTF-8 sequence inside IRI at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, source.Length, line, column, column));
                }

                source.Slice(position, byteCount).CopyTo(rented.AsSpan(written));
                written += byteCount;

                for(int i = 0; i < byteCount; i++)
                {
                    AdvanceByte(source[position]);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private uint ReadUcharEscape(ReadOnlySpan<byte> source, int escapeStart, int escapeLine, int escapeColumn)
    {
        //Decodes a \uXXXX or \UXXXXXXXX escape to a full Unicode scalar value. Each escape stands
        //for one scalar value: surrogate code points (U+D800..U+DFFF) are rejected — Turtle does
        //not encode astral characters as \u surrogate pairs, only as a single \U escape — and code
        //points beyond U+10FFFF are rejected as out of range.
        if(position + 1 >= source.Length)
        {
            throw new TurtleParseException(
                $"Truncated escape sequence at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, position, escapeLine, escapeColumn, column));
        }

        byte marker = source[position + 1];
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
                SourceSpan.SingleLine(escapeStart, position + 2, escapeLine, escapeColumn, column + 2));
        }

        if(position + 2 + hexCount > source.Length)
        {
            throw new TurtleParseException(
                $"Truncated \\{(char)marker} escape at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, source.Length, escapeLine, escapeColumn, column));
        }

        AdvanceBytes(2);

        uint codepoint = 0;
        for(int i = 0; i < hexCount; i++)
        {
            byte hex = source[position];
            if(!TryHexValue(hex, out uint digit))
            {
                throw new TurtleParseException(
                    $"Invalid hex digit '{(char)hex}' in escape at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
            }

            codepoint = (codepoint << 4) | digit;
            AdvanceByte(hex);
        }

        if(codepoint >= (uint)UnicodeConstants.SurrogateRangeFirst && codepoint <= (uint)UnicodeConstants.SurrogateRangeLast)
        {
            throw new TurtleParseException(
                $"Surrogate code point U+{codepoint:X4} is not a valid Unicode scalar value at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, position, escapeLine, escapeColumn, column));
        }

        if(codepoint > (uint)UnicodeConstants.MaximumCodePoint)
        {
            throw new TurtleParseException(
                $"Code point U+{codepoint:X} exceeds the Unicode range at line {escapeLine + 1} column {escapeColumn + 1}.",
                SourceSpan.SingleLine(escapeStart, position, escapeLine, escapeColumn, column));
        }

        return codepoint;
    }

    private TurtleToken LexShortString(ReadOnlySpan<byte> source, byte quote)
    {
        int startByte = position;
        int startLine = line;
        int startColumn = column;

        //Detect the three-quote long-string form.
        if(position + 2 < source.Length && source[position + 1] == quote && source[position + 2] == quote)
        {
            return LexLongString(source, quote);
        }

        AdvanceByte(quote);

        byte[] rented = ArrayPool<byte>.Shared.Rent(64);
        int written = 0;

        try
        {
            while(true)
            {
                if(position >= source.Length)
                {
                    throw new TurtleParseException(
                        $"Unterminated string literal starting at line {startLine + 1} column {startColumn + 1}.",
                        SourceSpan.SingleLine(startByte, position, startLine, startColumn, column));
                }

                byte b = source[position];

                if(b == quote)
                {
                    AdvanceByte(quote);
                    Utf8String value = Pool.Intern(rented.AsSpan(0, written));

                    return new TurtleToken(
                        TurtleTokenKind.StringLiteral,
                        CaptureSpan(startByte, position, startLine, startColumn, line, column),
                        value);
                }

                if(b == (byte)'\n' || b == (byte)'\r')
                {
                    throw new TurtleParseException(
                        $"Unescaped line break inside short string literal at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
                }

                if(b == (byte)'\\')
                {
                    written = DecodeEscape(source, ref rented, written);
                    continue;
                }

                int byteCount = Utf8ByteLength(b);
                if(position + byteCount > source.Length)
                {
                    throw new TurtleParseException(
                        $"Truncated UTF-8 sequence inside string literal at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, source.Length, line, column, column));
                }

                EnsureCapacity(ref rented, written + byteCount);
                source.Slice(position, byteCount).CopyTo(rented.AsSpan(written));
                written += byteCount;

                for(int i = 0; i < byteCount; i++)
                {
                    AdvanceByte(source[position]);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private TurtleToken LexLongString(ReadOnlySpan<byte> source, byte quote)
    {
        int startByte = position;
        int startLine = line;
        int startColumn = column;

        AdvanceBytes(3);

        byte[] rented = ArrayPool<byte>.Shared.Rent(128);
        int written = 0;

        try
        {
            while(true)
            {
                if(position >= source.Length)
                {
                    throw new TurtleParseException(
                        $"Unterminated long string literal starting at line {startLine + 1} column {startColumn + 1}.",
                        SourceSpan.SingleLine(startByte, position, startLine, startColumn, column));
                }

                byte b = source[position];

                if(b == quote && position + 2 < source.Length && source[position + 1] == quote && source[position + 2] == quote)
                {
                    AdvanceBytes(3);
                    Utf8String value = Pool.Intern(rented.AsSpan(0, written));

                    return new TurtleToken(
                        TurtleTokenKind.LongStringLiteral,
                        CaptureSpan(startByte, position, startLine, startColumn, line, column),
                        value);
                }

                if(b == (byte)'\\')
                {
                    written = DecodeEscape(source, ref rented, written);
                    continue;
                }

                int byteCount = Utf8ByteLength(b);
                if(position + byteCount > source.Length)
                {
                    throw new TurtleParseException(
                        $"Truncated UTF-8 sequence inside long string literal at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, source.Length, line, column, column));
                }

                EnsureCapacity(ref rented, written + byteCount);
                source.Slice(position, byteCount).CopyTo(rented.AsSpan(written));
                written += byteCount;

                for(int i = 0; i < byteCount; i++)
                {
                    AdvanceByte(source[position]);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private int DecodeEscape(ReadOnlySpan<byte> source, ref byte[] rented, int written)
    {
        if(position + 1 >= source.Length)
        {
            throw new TurtleParseException(
                $"Truncated escape sequence at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine(position, source.Length, line, column, column));
        }

        byte marker = source[position + 1];

        switch(marker)
        {
            case (byte)'t':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\t';
                AdvanceBytes(2);

                return written;
            }

            case (byte)'b':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = 0x08;
                AdvanceBytes(2);

                return written;
            }

            case (byte)'n':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\n';
                AdvanceBytes(2);

                return written;
            }

            case (byte)'r':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\r';
                AdvanceBytes(2);

                return written;
            }

            case (byte)'f':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = 0x0C;
                AdvanceBytes(2);

                return written;
            }

            case (byte)'"':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'"';
                AdvanceBytes(2);

                return written;
            }

            case (byte)'\'':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\'';
                AdvanceBytes(2);

                return written;
            }

            case (byte)'\\':
            {
                EnsureCapacity(ref rented, written + 1);
                rented[written++] = (byte)'\\';
                AdvanceBytes(2);

                return written;
            }

            case (byte)'u':
            case (byte)'U':
            {
                int escapeStart = position;
                int escapeLine = line;
                int escapeColumn = column;
                uint decoded = ReadUcharEscape(source, escapeStart, escapeLine, escapeColumn);

                return AppendCodepoint(decoded, ref rented, written);
            }

            default:
            {
                throw new TurtleParseException(
                    $"Invalid escape sequence '\\{(char)marker}' at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine(position, position + 2, line, column, column + 2));
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

    private TurtleToken LexBlankNodeLabel(ReadOnlySpan<byte> source)
    {
        if(position + 1 >= source.Length || source[position + 1] != (byte)':')
        {
            throw new TurtleParseException(
                $"Expected ':' after '_' at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
        }

        int startByte = position;
        int startLine = line;
        int startColumn = column;

        AdvanceBytes(2);

        int labelStart = position;
        if(position >= source.Length || !(IsPnCharsUOrDigit(source[position])))
        {
            throw new TurtleParseException(
                $"Expected blank-node label after '_:' at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
        }

        AdvanceByte(source[position]);

        //Greedy scan; a trailing '.' that is part of the statement terminator is rejected by re-stripping after the loop.
        while(position < source.Length && (IsPnChars(source[position]) || source[position] == (byte)'.'))
        {
            AdvanceByte(source[position]);
        }

        //A trailing '.' belongs to the statement terminator, not the label.
        while(position > labelStart && source[position - 1] == (byte)'.')
        {
            position--;
            column--;
        }

        Utf8String label = Pool.Intern(source.Slice(labelStart, position - labelStart));

        return new TurtleToken(
            TurtleTokenKind.BlankNodeLabel,
            CaptureSpan(startByte, position, startLine, startColumn, line, column),
            label);
    }

    private TurtleToken LexIdentifierLike(ReadOnlySpan<byte> source)
    {
        //Identifier-like tokens include: prefixed names (with or without an explicit prefix),
        //the rdf:type shorthand 'a', the boolean keywords, the SPARQL-style PREFIX / BASE / VERSION
        //keywords, and TriG's GRAPH keyword.
        int startByte = position;
        int startLine = line;
        int startColumn = column;
        byte first = source[position];

        if(first == (byte)':')
        {
            return LexPrefixedNameStartingWithColon(source, startByte, startLine, startColumn);
        }

        if(!IsPnCharsBase(first))
        {
            throw new TurtleParseException(
                $"Unexpected byte 0x{first:X2} at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
        }

        //Scan a PN_PREFIX-like identifier.
        AdvanceByte(first);
        while(position < source.Length && (IsPnChars(source[position]) || source[position] == (byte)'.'))
        {
            AdvanceByte(source[position]);
        }

        while(position > startByte && source[position - 1] == (byte)'.')
        {
            position--;
            column--;
        }

        ReadOnlySpan<byte> identifierSpan = source.Slice(startByte, position - startByte);

        //Distinguish a prefix declaration vs. a prefixed-name vs. a keyword by what follows.
        if(position < source.Length && source[position] == (byte)':')
        {
            AdvanceByte((byte)':');

            return LexPrefixedNameOrNamespace(source, startByte, startLine, startColumn);
        }

        if(identifierSpan.SequenceEqual("a"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.A,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("a"u8));
        }

        if(identifierSpan.SequenceEqual("true"u8) || identifierSpan.SequenceEqual("false"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.BooleanLiteral,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern(identifierSpan));
        }

        if(EqualsIgnoreAsciiCase(identifierSpan, "PREFIX"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.PrefixKeyword,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("PREFIX"u8));
        }

        if(EqualsIgnoreAsciiCase(identifierSpan, "BASE"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.BaseKeyword,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("BASE"u8));
        }

        if(EqualsIgnoreAsciiCase(identifierSpan, "VERSION"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.VersionKeyword,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("VERSION"u8));
        }

        if(identifierSpan.SequenceEqual("GRAPH"u8))
        {
            return new TurtleToken(
                TurtleTokenKind.GraphKeyword,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern("GRAPH"u8));
        }

        throw new TurtleParseException(
            $"Unrecognised identifier '{Encoding.UTF8.GetString(identifierSpan)}' at line {startLine + 1} column {startColumn + 1}.",
            CaptureSpan(startByte, position, startLine, startColumn, line, column));
    }

    private TurtleToken LexPrefixedNameStartingWithColon(
        ReadOnlySpan<byte> source,
        int startByte,
        int startLine,
        int startColumn)
    {
        AdvanceByte((byte)':');

        return LexPrefixedNameOrNamespace(source, startByte, startLine, startColumn);
    }

    private TurtleToken LexPrefixedNameOrNamespace(
        ReadOnlySpan<byte> source,
        int startByte,
        int startLine,
        int startColumn)
    {
        int localStart = position;
        bool firstByte = true;

        while(position < source.Length)
        {
            byte b = source[position];
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
                if(position + 1 >= source.Length)
                {
                    throw new TurtleParseException(
                        $"Truncated escape inside prefixed name at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, position + 1, line, column, column + 1));
                }

                AdvanceBytes(2);
                firstByte = false;
                continue;
            }

            if(b == (byte)'%')
            {
                //Percent-encoded byte: '%' HEX HEX.
                if(position + 2 >= source.Length
                   || !TryHexValue(source[position + 1], out _)
                   || !TryHexValue(source[position + 2], out _))
                {
                    throw new TurtleParseException(
                        $"Malformed percent escape inside prefixed name at line {line + 1} column {column + 1}.",
                        SourceSpan.SingleLine(position, position + 3, line, column, column + 3));
                }

                AdvanceBytes(3);
                firstByte = false;
                continue;
            }

            AdvanceByte(b);
            firstByte = false;
        }

        //A trailing '.' is part of the statement terminator, not the local name.
        while(position > localStart && source[position - 1] == (byte)'.')
        {
            position--;
            column--;
        }

        int totalLength = position - startByte;
        ReadOnlySpan<byte> fullSpan = source.Slice(startByte, totalLength);

        //If nothing followed the colon, this is a PNAME_NS (prefix declaration form).
        if(position == localStart)
        {
            return new TurtleToken(
                TurtleTokenKind.PrefixNamespace,
                CaptureSpan(startByte, position, startLine, startColumn, line, column),
                Pool.Intern(fullSpan));
        }

        return new TurtleToken(
            TurtleTokenKind.PrefixedName,
            CaptureSpan(startByte, position, startLine, startColumn, line, column),
            Pool.Intern(fullSpan));
    }

    private TurtleToken LexNumericSigned(ReadOnlySpan<byte> source)
    {
        int startByte = position;
        int startLine = line;
        int startColumn = column;

        byte sign = source[position];
        AdvanceByte(sign);

        if(position >= source.Length || (!IsDigit(source[position]) && source[position] != (byte)'.'))
        {
            throw new TurtleParseException(
                $"Expected digit after '{(char)sign}' at line {line + 1} column {column + 1}.",
                SourceSpan.SingleLine(startByte, position, startLine, startColumn, column));
        }

        return ContinueNumeric(source, startByte, startLine, startColumn);
    }

    private TurtleToken LexNumericUnsigned(ReadOnlySpan<byte> source)
    {
        int startByte = position;
        int startLine = line;
        int startColumn = column;

        return ContinueNumeric(source, startByte, startLine, startColumn);
    }

    private TurtleToken ContinueNumeric(
        ReadOnlySpan<byte> source,
        int startByte,
        int startLine,
        int startColumn)
    {
        bool sawDecimal = false;
        bool sawExponent = false;
        bool sawDigit = false;

        while(position < source.Length && IsDigit(source[position]))
        {
            sawDigit = true;
            AdvanceByte(source[position]);
        }

        if(position < source.Length && source[position] == (byte)'.')
        {
            //A '.' that does not precede a digit belongs to the statement terminator.
            if(position + 1 < source.Length && IsDigit(source[position + 1]))
            {
                sawDecimal = true;
                AdvanceByte((byte)'.');

                while(position < source.Length && IsDigit(source[position]))
                {
                    sawDigit = true;
                    AdvanceByte(source[position]);
                }
            }
        }

        if(position < source.Length && (source[position] == (byte)'e' || source[position] == (byte)'E'))
        {
            sawExponent = true;
            AdvanceByte(source[position]);

            if(position < source.Length && (source[position] == (byte)'+' || source[position] == (byte)'-'))
            {
                AdvanceByte(source[position]);
            }

            int expStart = position;
            while(position < source.Length && IsDigit(source[position]))
            {
                AdvanceByte(source[position]);
            }

            if(position == expStart)
            {
                throw new TurtleParseException(
                    $"Expected exponent digits at line {line + 1} column {column + 1}.",
                    SourceSpan.SingleLine(startByte, position, startLine, startColumn, column));
            }
        }

        if(!sawDigit)
        {
            throw new TurtleParseException(
                $"Invalid numeric literal at line {startLine + 1} column {startColumn + 1}.",
                CaptureSpan(startByte, position, startLine, startColumn, line, column));
        }

        TurtleTokenKind kind = (sawExponent, sawDecimal) switch
        {
            (true, _) => TurtleTokenKind.DoubleLiteral,
            (false, true) => TurtleTokenKind.DecimalLiteral,
            (false, false) => TurtleTokenKind.IntegerLiteral
        };

        Utf8String value = Pool.Intern(source.Slice(startByte, position - startByte));

        return new TurtleToken(
            kind,
            CaptureSpan(startByte, position, startLine, startColumn, line, column),
            value);
    }

    private void SkipWhitespaceAndComments()
    {
        ReadOnlySpan<byte> source = SourceMemory.Span;

        while(position < source.Length)
        {
            byte b = source[position];

            if(IsWhitespaceByte(b))
            {
                AdvanceByte(b);
                continue;
            }

            if(b == (byte)'#')
            {
                while(position < source.Length && source[position] != (byte)'\n' && source[position] != (byte)'\r')
                {
                    AdvanceByte(source[position]);
                }

                continue;
            }

            break;
        }
    }

    private void AdvanceByte(byte b)
    {
        position++;

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

            if(position < SourceMemory.Length && SourceMemory.Span[position] == (byte)'\n')
            {
                position++;
            }
        }
        else
        {
            column++;
        }
    }

    private void AdvanceBytes(int count)
    {
        for(int i = 0; i < count; i++)
        {
            AdvanceByte(SourceMemory.Span[position]);
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

    private static bool EqualsIgnoreAsciiCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if(left.Length != right.Length)
        {
            return false;
        }

        for(int i = 0; i < left.Length; i++)
        {
            byte a = left[i];
            byte b = right[i];

            if(a >= (byte)'A' && a <= (byte)'Z')
            {
                a = (byte)(a + ('a' - 'A'));
            }

            if(b >= (byte)'A' && b <= (byte)'Z')
            {
                b = (byte)(b + ('a' - 'A'));
            }

            if(a != b)
            {
                return false;
            }
        }

        return true;
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
    internal int Position => position;
}
