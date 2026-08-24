using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>
/// Reads an OWL 2 Manchester-syntax document fed incrementally, chunk by
/// chunk, over UTF-8 bytes.
/// </summary>
/// <remarks>
/// <para>
/// The reader implements the project's editor contract: after each
/// <see cref="Feed"/> the <see cref="Status"/> reports whether the input so
/// far ends where a document may validly end
/// (<see cref="IncrementalParseStatus.Complete"/>) or inside an unfinished
/// construct (<see cref="IncrementalParseStatus.NeedMore"/>) — a suspended
/// token, an open group, or a trailing token that grammatically requires a
/// continuation, such as a frame or section keyword, a comma, or an infix
/// operator word. Incompleteness is a status, never a diagnostic. Truncation
/// becomes an error only when <see cref="Complete"/> declares the input
/// final, which is how the whole-buffer facade
/// <see cref="OwlManchesterSyntaxReader.Read(System.ReadOnlySpan{byte})"/>
/// consumes the same machinery.
/// </para>
/// <para>
/// The tokenizer is resumable: it commits at token boundaries and re-lexes a
/// suspended token once more bytes arrive. Frames convert when
/// <see cref="Complete"/> runs, in two passes — frame subjects declare their
/// entities first, then sections convert to axioms — because Manchester
/// expressions are typed by the declaration census (an undeclared property in
/// a restriction reads as an object property). Comments run from <c>#</c> to
/// the end of the line.
/// </para>
/// <para>
/// The grammar's delimiters are all ASCII, and UTF-8 is self-synchronizing,
/// so a multi-byte code point can never masquerade as a delimiter and a chunk
/// boundary inside one resumes correctly. Token values are zero-copy windows
/// over <see cref="Buffer"/>; only a decoded literal value carries its own
/// bytes.
/// </para>
/// </remarks>
public sealed class OwlManchesterSyntaxIncrementalReader
{
    /// <summary>The document bytes fed so far.</summary>
    private byte[] Buffer { get; set; } = new byte[256];

    /// <summary>The number of valid bytes in <see cref="Buffer"/>.</summary>
    private int Length { get; set; }

    /// <summary>The committed lex position: the start of the next unlexed content.</summary>
    private int LexPosition { get; set; }

    /// <summary>Whether the last drain suspended mid-token waiting for more bytes.</summary>
    private bool LexSuspended { get; set; }

    /// <summary>Whether <see cref="Complete"/> has declared the input final.</summary>
    private bool Final { get; set; }

    /// <summary>The completed document, built once by <see cref="Complete"/>.</summary>
    private OwlOntologyDocument? Document { get; set; }

    /// <summary>Maps byte offsets to line-column source spans.</summary>
    private ByteSourceMap Map { get; } = new();

    /// <summary>The bag every lexical, structural, and conversion diagnostic accumulates into.</summary>
    private DiagnosticBag Bag { get; } = new();

    /// <summary>The tree root standing for the document; its direct children are the top-level tokens and groups.</summary>
    private OwlManchesterNode Root { get; } = new();

    /// <summary>The open-group stack; <see cref="Root"/> sits at the bottom for the whole parse.</summary>
    private Stack<OwlManchesterNode> Open { get; } = new();

    /// <summary>The exclusive end offset of the last accepted token; unterminated groups span to it.</summary>
    private int LastTokenEnd { get; set; }

    /// <summary>Initialises an empty reader; feed source bytes through <see cref="Feed"/>.</summary>
    public OwlManchesterSyntaxIncrementalReader()
    {
        Open.Push(Root);
    }

    /// <summary>
    /// Gets the diagnostics recorded so far. While the input is incomplete the
    /// bag holds only genuine faults — an unfinished tail is reported through
    /// <see cref="Status"/>, never here.
    /// </summary>
    public DiagnosticBag Diagnostics => Bag;

    /// <summary>
    /// Gets whether the input fed so far ends where a document may validly
    /// end or inside an unfinished construct.
    /// </summary>
    public IncrementalParseStatus Status =>
        LexSuspended || Open.Count > 1 || TailNeedsContinuation()
            ? IncrementalParseStatus.NeedMore
            : IncrementalParseStatus.Complete;

    /// <summary>
    /// Appends source bytes and lexes as far as the input now permits.
    /// </summary>
    /// <param name="chunk">The next run of document bytes, of any length.</param>
    /// <returns>The <see cref="Status"/> after the chunk is consumed.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> has already been called.</exception>
    public IncrementalParseStatus Feed(ReadOnlySpan<byte> chunk)
    {
        if(Final)
        {
            throw new InvalidOperationException("The reader has been completed; no more input can be fed.");
        }

        Append(chunk);
        Drain();

        return Status;
    }

    /// <summary>
    /// Declares the input final, converts the frames, and returns the
    /// structural document. From here on truncation is an error: an
    /// unterminated token reports a lexical diagnostic and every unterminated
    /// group reports an unbalanced delimiter, while the content parsed so far
    /// is kept.
    /// </summary>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    public OwlOntologyDocument Complete()
    {
        if(Document is OwlOntologyDocument document)
        {
            return document;
        }

        Final = true;
        Drain();
        CloseUnterminatedGroups();

        OwlManchesterSyntaxConverter converter = new(Bag);
        converter.ConvertDocument(Root.Children);

        Document = new OwlOntologyDocument(
            converter.Axioms.ToImmutable(),
            converter.OntologyIri,
            Bag,
            converter.DeclaredClasses,
            converter.DeclaredObjectProperties,
            converter.DeclaredDataProperties,
            converter.DeclaredAnnotationProperties,
            converter.DeclaredDatatypes);

        return Document;
    }

    /// <summary>Appends a chunk to the byte buffer and extends the source map over it.</summary>
    /// <param name="chunk">The bytes to append.</param>
    private void Append(ReadOnlySpan<byte> chunk)
    {
        if(Length + chunk.Length > Buffer.Length)
        {
            byte[] grown = new byte[Math.Max(Buffer.Length * 2, Length + chunk.Length)];
            Buffer.AsSpan(0, Length).CopyTo(grown);
            Buffer = grown;
        }

        chunk.CopyTo(Buffer.AsSpan(Length));
        Length += chunk.Length;
        Map.Append(chunk);
    }

    /// <summary>Lexes and accepts tokens until the buffer is exhausted or a token suspends.</summary>
    private void Drain()
    {
        LexSuspended = false;

        while(true)
        {
            LexStep step = TryLexToken(out OwlManchesterToken token);

            if(step == LexStep.Exhausted)
            {
                return;
            }

            if(step == LexStep.NeedMore)
            {
                LexSuspended = true;

                return;
            }

            Accept(token);
        }
    }

    /// <summary>The outcome of one resumable lexing step.</summary>
    private enum LexStep
    {
        /// <summary>A token was lexed and the position committed past it.</summary>
        Token,

        /// <summary>The buffer ended mid-token or mid-comment; more bytes decide it, so the position holds at the token start.</summary>
        NeedMore,

        /// <summary>The buffer is exhausted at a clean boundary; no token remains.</summary>
        Exhausted
    }

    //The resumable tokenizer core, the same commitment discipline as the
    //functional-syntax reader: whitespace and complete comments commit as they
    //pass; a token commits only when its end is fixed by a delimiter or, in
    //final mode, the end of input; a suspended token re-lexes from its start
    //once more bytes arrive. The Manchester-specific corner is '<': it opens an
    //IRI reference unless what follows makes it a facet comparison ('<=', or
    //'<' before whitespace, a digit, a sign, or a quote).
    private LexStep TryLexToken(out OwlManchesterToken token)
    {
        token = default;
        ReadOnlySpan<byte> text = Buffer.AsSpan(0, Length);
        int i = LexPosition;

        while(true)
        {
            while(i < text.Length && IsAsciiWhitespace(text[i]))
            {
                i++;
            }

            LexPosition = i;

            if(i == text.Length)
            {
                return LexStep.Exhausted;
            }

            byte c = text[i];

            if(c == (byte)'#')
            {
                int newline = text[i..].IndexOf((byte)'\n');
                if(newline < 0)
                {
                    if(!Final)
                    {
                        return LexStep.NeedMore;
                    }

                    LexPosition = text.Length;

                    return LexStep.Exhausted;
                }

                i += newline + 1;

                continue;
            }

            switch(c)
            {
                case((byte)','):
                {
                    return Single(OwlManchesterTokenKind.Comma, i, 1, out token);
                }
                case((byte)'('):
                {
                    return Single(OwlManchesterTokenKind.Open, i, 1, out token);
                }
                case((byte)')'):
                {
                    return Single(OwlManchesterTokenKind.Close, i, 1, out token);
                }
                case((byte)'{'):
                {
                    return Single(OwlManchesterTokenKind.OpenBrace, i, 1, out token);
                }
                case((byte)'}'):
                {
                    return Single(OwlManchesterTokenKind.CloseBrace, i, 1, out token);
                }
                case((byte)'['):
                {
                    return Single(OwlManchesterTokenKind.OpenBracket, i, 1, out token);
                }
                case((byte)']'):
                {
                    return Single(OwlManchesterTokenKind.CloseBracket, i, 1, out token);
                }
                default:
                {
                    break;
                }
            }

            if(c == (byte)'>')
            {
                if(i + 1 == text.Length && !Final)
                {
                    //A '>' at the buffer end may become '>='.
                    return LexStep.NeedMore;
                }

                if(i + 1 < text.Length && text[i + 1] == (byte)'=')
                {
                    return Single(OwlManchesterTokenKind.Comparison, i, 2, out token);
                }

                return Single(OwlManchesterTokenKind.Comparison, i, 1, out token);
            }

            if(c == (byte)'<')
            {
                if(i + 1 == text.Length && !Final)
                {
                    //A '<' at the buffer end may become '<=' or open an IRI.
                    return LexStep.NeedMore;
                }

                if(i + 1 < text.Length && text[i + 1] == (byte)'=')
                {
                    return Single(OwlManchesterTokenKind.Comparison, i, 2, out token);
                }

                if(i + 1 == text.Length || IsAsciiWhitespace(text[i + 1]) || IsAsciiDigit(text[i + 1])
                    || text[i + 1] is (byte)'+' or (byte)'-' or (byte)'"')
                {
                    return Single(OwlManchesterTokenKind.Comparison, i, 1, out token);
                }

                return LexIri(text, i, out token);
            }

            if(c == (byte)'"')
            {
                return LexLiteral(text, i, out token);
            }

            if(c == (byte)'_' && i + 1 == text.Length && !Final)
            {
                //A lone underscore at the buffer end may become _:label or a name.
                return LexStep.NeedMore;
            }

            if(c == (byte)'_' && i + 1 < text.Length && text[i + 1] == (byte)':')
            {
                int start = i + 2;
                int end = start;
                while(end < text.Length && IsNameByte(text[end]))
                {
                    end++;
                }

                if(end == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                token = new OwlManchesterToken(OwlManchesterTokenKind.BlankNode, Slice(start, end), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            if(c is (byte)'+' or (byte)'-')
            {
                if(i + 1 == text.Length && !Final)
                {
                    //A sign at the buffer end may begin a number.
                    return LexStep.NeedMore;
                }

                if(i + 1 < text.Length && (IsAsciiDigit(text[i + 1]) || text[i + 1] == (byte)'.'))
                {
                    return LexNumber(text, i, out token);
                }

                Report($"Unexpected character '{(char)c}'.", Map.Span(i, i + 1));
                i++;
                LexPosition = i;

                continue;
            }

            if(IsAsciiDigit(c) || (c == (byte)'.' && i + 1 < text.Length && IsAsciiDigit(text[i + 1])))
            {
                return LexNumber(text, i, out token);
            }

            if(c == (byte)'.' && i + 1 == text.Length && !Final)
            {
                //A dot at the buffer end may begin '.5'.
                return LexStep.NeedMore;
            }

            if(IsNameStartByte(c) || c == (byte)':')
            {
                int end = i;
                while(end < text.Length && (IsNameByte(text[end]) || text[end] == (byte)':'))
                {
                    end++;
                }

                if(end == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                token = new OwlManchesterToken(OwlManchesterTokenKind.Name, Slice(i, end), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            Report($"Unexpected character '{(char)c}'.", Map.Span(i, i + 1));
            i++;
            LexPosition = i;
        }
    }

    /// <summary>Emits a fixed-length punctuation token and commits past it.</summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="start">The token's start offset.</param>
    /// <param name="length">The token's byte length.</param>
    /// <param name="token">The emitted token.</param>
    /// <returns>Always <see cref="LexStep.Token"/>.</returns>
    private LexStep Single(OwlManchesterTokenKind kind, int start, int length, out OwlManchesterToken token)
    {
        token = new OwlManchesterToken(kind, Slice(start, start + length), null, null, start, start + length);
        LexPosition = token.End;

        return LexStep.Token;
    }

    /// <summary>Lexes a <c>&lt;…&gt;</c> IRI reference starting at <paramref name="start"/>.</summary>
    /// <param name="text">The buffered bytes.</param>
    /// <param name="start">The offset of the opening angle bracket.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexIri(ReadOnlySpan<byte> text, int start, out OwlManchesterToken token)
    {
        token = default;

        int close = text[(start + 1)..].IndexOf((byte)'>');
        if(close < 0)
        {
            if(!Final)
            {
                return LexStep.NeedMore;
            }

            Report("Unterminated IRI reference.", Map.Span(start, text.Length));
            LexPosition = text.Length;

            return LexStep.Exhausted;
        }

        token = new OwlManchesterToken(OwlManchesterTokenKind.Iri, Slice(start + 1, start + 1 + close), null, null, start, start + close + 2);
        LexPosition = token.End;

        return LexStep.Token;
    }

    /// <summary>Lexes a quoted literal with its optional <c>^^datatype</c> or <c>@language</c> suffix.</summary>
    /// <param name="text">The buffered bytes.</param>
    /// <param name="start">The offset of the opening quote.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexLiteral(ReadOnlySpan<byte> text, int start, out OwlManchesterToken token)
    {
        token = default;

        //The decoded value is never longer than its source extent; escapes
        //only ever shrink it, so the remaining span bounds the buffer.
        byte[] value = new byte[text.Length - start];
        int valueLength = 0;
        int i = start + 1;
        while(i < text.Length && text[i] != (byte)'"')
        {
            if(text[i] == (byte)'\\' && i + 1 < text.Length)
            {
                i++;
                value[valueLength++] = text[i] switch
                {
                    (byte)'n' => (byte)'\n',
                    (byte)'t' => (byte)'\t',
                    (byte)'r' => (byte)'\r',
                    _ => text[i]
                };
            }
            else
            {
                value[valueLength++] = text[i];
            }

            i++;
        }

        if(i == text.Length)
        {
            if(!Final)
            {
                return LexStep.NeedMore;
            }

            Report("Unterminated string literal.", Map.Span(start, text.Length));
            LexPosition = text.Length;

            return LexStep.Exhausted;
        }

        i++;

        //The buffer ending right after the closing quote, or inside a
        //potential suffix, leaves the token extent undecided.
        if(!Final && i == text.Length)
        {
            return LexStep.NeedMore;
        }

        Utf8String? datatype = null;
        Utf8String? language = null;

        if(i + 1 < text.Length && text[i] == (byte)'^' && text[i + 1] == (byte)'^')
        {
            i += 2;

            if(i == text.Length && !Final)
            {
                return LexStep.NeedMore;
            }

            if(i < text.Length && text[i] == (byte)'<')
            {
                int close = text[(i + 1)..].IndexOf((byte)'>');
                if(close < 0)
                {
                    if(!Final)
                    {
                        return LexStep.NeedMore;
                    }

                    Report("Unterminated datatype IRI.", Map.Span(i, text.Length));
                    datatype = Utf8String.WithoutPrecomputedHash(default);
                    i = text.Length;
                }
                else
                {
                    datatype = MarkedDatatypeIri(text, i + 1, close);
                    i += close + 2;
                }
            }
            else
            {
                int nameStart = i;
                while(i < text.Length && (IsNameByte(text[i]) || text[i] == (byte)':'))
                {
                    i++;
                }

                if(i == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                datatype = Slice(nameStart, i);
            }
        }
        else if(!Final && i + 1 == text.Length && text[i] == (byte)'^')
        {
            //A single caret at the buffer end may become the ^^ of a datatype.
            return LexStep.NeedMore;
        }
        else if(i < text.Length && text[i] == (byte)'@')
        {
            i++;
            int tagStart = i;
            while(i < text.Length && (IsAsciiLetterOrDigit(text[i]) || text[i] == (byte)'-'))
            {
                i++;
            }

            if(i == text.Length && !Final)
            {
                return LexStep.NeedMore;
            }

            language = Slice(tagStart, i);
        }

        token = new OwlManchesterToken(OwlManchesterTokenKind.Literal, new Utf8String(value.AsMemory(0, valueLength)), datatype, language, start, i);
        LexPosition = i;

        return LexStep.Token;
    }

    /// <summary>
    /// Lexes a numeric literal: optional sign, digits with an optional
    /// fraction (or a bare <c>.digits</c> fraction), an optional exponent,
    /// and an optional <c>f</c> suffix. The raw lexical is kept; the
    /// converter infers the datatype from its shape.
    /// </summary>
    /// <param name="text">The buffered bytes.</param>
    /// <param name="start">The offset the number starts at.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexNumber(ReadOnlySpan<byte> text, int start, out OwlManchesterToken token)
    {
        token = default;

        //The caller guarantees a sign is followed by a digit or a dot.
        int i = start;
        if(text[i] is (byte)'+' or (byte)'-')
        {
            i++;
        }

        while(i < text.Length && IsAsciiDigit(text[i]))
        {
            i++;
        }

        if(i < text.Length && text[i] == (byte)'.' && i + 1 < text.Length && IsAsciiDigit(text[i + 1]))
        {
            i++;
            while(i < text.Length && IsAsciiDigit(text[i]))
            {
                i++;
            }
        }
        else if(i < text.Length && text[i] == (byte)'.' && i + 1 == text.Length && !Final)
        {
            return LexStep.NeedMore;
        }

        if(i < text.Length && text[i] is (byte)'e' or (byte)'E')
        {
            int exponent = i + 1;
            if(exponent < text.Length && text[exponent] is (byte)'+' or (byte)'-')
            {
                exponent++;
            }

            if(exponent == text.Length && !Final)
            {
                return LexStep.NeedMore;
            }

            if(exponent < text.Length && IsAsciiDigit(text[exponent]))
            {
                i = exponent;
                while(i < text.Length && IsAsciiDigit(text[i]))
                {
                    i++;
                }
            }
        }

        if(i < text.Length && text[i] is (byte)'f' or (byte)'F')
        {
            i++;
        }

        if(i == text.Length && !Final)
        {
            //More digits, an exponent, or a suffix may still arrive.
            return LexStep.NeedMore;
        }

        token = new OwlManchesterToken(OwlManchesterTokenKind.Number, Slice(start, i), null, null, start, i);
        LexPosition = i;

        return LexStep.Token;
    }

    /// <summary>Captures a zero-copy window over <see cref="Buffer"/> as a deferred-hash value.</summary>
    /// <param name="start">The inclusive start byte offset.</param>
    /// <param name="end">The exclusive end byte offset.</param>
    /// <returns>The bytes <c>[start, end)</c> as a <see cref="Utf8String"/> over the buffer's memory.</returns>
    private Utf8String Slice(int start, int end)
    {
        return Utf8String.WithoutPrecomputedHash(Buffer.AsMemory(start, end - start));
    }

    /// <summary>Builds a <c>&lt;</c>-marked datatype IRI value: the marker byte followed by the IRI body.</summary>
    /// <param name="text">The buffered bytes.</param>
    /// <param name="bodyStart">The inclusive start byte offset of the IRI body.</param>
    /// <param name="bodyLength">The length of the IRI body in bytes.</param>
    /// <returns>The marked datatype IRI as its own buffer; the converter strips the marker.</returns>
    private static Utf8String MarkedDatatypeIri(ReadOnlySpan<byte> text, int bodyStart, int bodyLength)
    {
        byte[] marked = new byte[bodyLength + 1];
        marked[0] = (byte)'<';
        text.Slice(bodyStart, bodyLength).CopyTo(marked.AsSpan(1));

        return Utf8String.WithoutPrecomputedHash(marked);
    }

    /// <summary>Whether a byte is one of the grammar's ASCII whitespace bytes.</summary>
    /// <param name="b">The byte to classify.</param>
    /// <returns><see langword="true"/> for space, tab, line feed, vertical tab, form feed, or carriage return.</returns>
    private static bool IsAsciiWhitespace(byte b)
    {
        return b == (byte)' ' || (b >= (byte)'\t' && b <= (byte)'\r');
    }

    /// <summary>Whether a byte is an ASCII decimal digit.</summary>
    /// <param name="b">The byte to classify.</param>
    /// <returns><see langword="true"/> for <c>0</c> through <c>9</c>.</returns>
    private static bool IsAsciiDigit(byte b)
    {
        return b >= (byte)'0' && b <= (byte)'9';
    }

    /// <summary>Whether a byte is an ASCII letter or decimal digit.</summary>
    /// <param name="b">The byte to classify.</param>
    /// <returns><see langword="true"/> for <c>A</c>–<c>Z</c>, <c>a</c>–<c>z</c>, or <c>0</c>–<c>9</c>.</returns>
    private static bool IsAsciiLetterOrDigit(byte b)
    {
        return IsAsciiDigit(b) || ((b | 0x20) >= (byte)'a' && (b | 0x20) <= (byte)'z');
    }

    /// <summary>Whether a byte can begin a name: an ASCII letter, an underscore, or any non-ASCII (multi-byte) code-point byte.</summary>
    /// <param name="b">The byte to classify.</param>
    /// <returns><see langword="true"/> when the byte may start a word or prefixed name.</returns>
    private static bool IsNameStartByte(byte b)
    {
        return ((b | 0x20) >= (byte)'a' && (b | 0x20) <= (byte)'z') || b == (byte)'_' || b >= 0x80;
    }

    /// <summary>Whether a byte can continue a name: an ASCII letter or digit, <c>_</c>, <c>-</c>, <c>.</c>, or any non-ASCII byte.</summary>
    /// <param name="b">The byte to classify.</param>
    /// <returns><see langword="true"/> when the byte may continue a word or prefixed name.</returns>
    private static bool IsNameByte(byte b)
    {
        return IsAsciiLetterOrDigit(b) || b == (byte)'_' || b == (byte)'-' || b == (byte)'.' || b >= 0x80;
    }

    //The streaming tree builder: groups nest by bracket family, everything
    //else lands as an atom in the innermost open group.
    private void Accept(OwlManchesterToken token)
    {
        LastTokenEnd = token.End;

        if(token.Kind is OwlManchesterTokenKind.Open or OwlManchesterTokenKind.OpenBrace or OwlManchesterTokenKind.OpenBracket)
        {
            OwlManchesterGroupKind kind = token.Kind switch
            {
                OwlManchesterTokenKind.Open => OwlManchesterGroupKind.Paren,
                OwlManchesterTokenKind.OpenBrace => OwlManchesterGroupKind.Brace,
                _ => OwlManchesterGroupKind.Bracket
            };

            OwlManchesterNode group = new() { GroupKind = kind, SpanStart = token.Start };
            Open.Peek().Children.Add(group);
            Open.Push(group);

            return;
        }

        if(token.Kind is OwlManchesterTokenKind.Close or OwlManchesterTokenKind.CloseBrace or OwlManchesterTokenKind.CloseBracket)
        {
            OwlManchesterGroupKind kind = token.Kind switch
            {
                OwlManchesterTokenKind.Close => OwlManchesterGroupKind.Paren,
                OwlManchesterTokenKind.CloseBrace => OwlManchesterGroupKind.Brace,
                _ => OwlManchesterGroupKind.Bracket
            };

            if(Open.Count == 1 || Open.Peek().GroupKind != kind)
            {
                Report($"Unbalanced '{token.Text}'.", Map.Span(token.Start, token.End));

                return;
            }

            OwlManchesterNode closed = Open.Pop();
            closed.Span = Map.Span(closed.SpanStart, token.End);

            return;
        }

        Open.Peek().Children.Add(new OwlManchesterNode
        {
            IsAtom = true,
            Atom = token,
            Span = Map.Span(token.Start, token.End)
        });
    }

    /// <summary>
    /// Closes every group the final input left unterminated, innermost first:
    /// each spans to the last token seen and reports an unbalanced delimiter,
    /// while its children stay available to the conversion.
    /// </summary>
    private void CloseUnterminatedGroups()
    {
        while(Open.Count > 1)
        {
            OwlManchesterNode unterminated = Open.Pop();
            unterminated.Span = Map.Span(unterminated.SpanStart, LastTokenEnd);
            string opener = unterminated.GroupKind switch
            {
                OwlManchesterGroupKind.Paren => "(",
                OwlManchesterGroupKind.Brace => "{",
                _ => "["
            };

            Report($"Unbalanced '{opener}' at end of document.", unterminated.Span);
        }
    }

    /// <summary>
    /// Decides whether the last top-level token grammatically requires a
    /// continuation: a frame or section keyword awaiting content, a comma, an
    /// infix or prefix operator word, or a prefix declaration awaiting its
    /// IRI.
    /// </summary>
    /// <returns><see langword="true"/> when the tail cannot validly end a document.</returns>
    private bool TailNeedsContinuation()
    {
        List<OwlManchesterNode> items = Root.Children;
        if(items.Count == 0)
        {
            return false;
        }

        OwlManchesterNode last = items[^1];
        if(!last.IsAtom)
        {
            return false;
        }

        OwlManchesterToken token = last.Atom;

        if(token.Kind is OwlManchesterTokenKind.Comma or OwlManchesterTokenKind.Comparison)
        {
            return true;
        }

        if(token.Kind != OwlManchesterTokenKind.Name)
        {
            //An IRI right after 'Prefix: p:' still awaits nothing — the IRI
            //completes the declaration — so closing atoms end cleanly.
            return false;
        }

        if(OwlManchesterWords.IsContinuationKeyword(token.Text))
        {
            return true;
        }

        //The name naming a prefix inside 'Prefix: p: <iri>' awaits its IRI.
        return items.Count >= 2
            && items[^2] is { IsAtom: true } previous
            && previous.Atom.Kind == OwlManchesterTokenKind.Name
            && OwlManchesterWords.IsPrefixKeyword(previous.Atom.Text);
    }

    /// <summary>Records an error diagnostic into the shared bag.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="span">The source extent the diagnostic covers.</param>
    private void Report(string message, SourceSpan span)
    {
        Bag.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            span,
            Utf8Strings.From(message)));
    }
}
