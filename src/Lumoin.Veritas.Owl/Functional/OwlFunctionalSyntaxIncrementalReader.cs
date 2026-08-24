using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// Reads an OWL 2 functional-style syntax document fed incrementally,
/// chunk by chunk, over UTF-8 bytes.
/// </summary>
/// <remarks>
/// <para>
/// The reader implements the project's editor contract: after each
/// <see cref="Feed"/> the <see cref="Status"/> reports whether the input so
/// far ends at a document boundary (<see cref="IncrementalParseStatus.Complete"/>)
/// or inside a token, group, or undecided constructor head
/// (<see cref="IncrementalParseStatus.NeedMore"/>). Incompleteness is a
/// status, never a diagnostic — an editor must not mark a merely-unfinished
/// tail as an error. Truncation becomes an error only when
/// <see cref="Complete"/> declares the input final, which is how the
/// whole-buffer facade <see cref="OwlFunctionalSyntaxReader.Read(System.ReadOnlySpan{byte})"/>
/// consumes the same machinery.
/// </para>
/// <para>
/// The core is resumable: the tokenizer commits at token boundaries and
/// re-lexes a suspended token once more bytes arrive; the constructor tree
/// keeps its open-group stack between feeds; and each completed child of the
/// <c>Ontology(…)</c> group converts — and releases its subtree — as soon as
/// its closing parenthesis arrives, so peak parse state tracks the largest
/// single axiom rather than the document. All passes run on explicit stacks.
/// </para>
/// <para>
/// The grammar's structural delimiters are all ASCII, and UTF-8 is
/// self-synchronizing, so a multi-byte code point can never masquerade as a
/// delimiter: scanning for a byte such as <c>"</c>, <c>&gt;</c>, or <c>)</c>
/// is exact even when a chunk boundary falls inside a multi-byte code point.
/// Token values are zero-copy windows over <see cref="Buffer"/>; only a
/// decoded literal value carries its own bytes.
/// </para>
/// </remarks>
public sealed class OwlFunctionalSyntaxIncrementalReader
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

    /// <summary>The structural converter completed constructor groups stream into.</summary>
    private OwlFunctionalSyntaxConverter Converter { get; } = new();

    /// <summary>The tree root standing for the document; its direct children are the top-level groups.</summary>
    private OwlFunctionalNode Root { get; } = new() { Head = null };

    /// <summary>The open-group stack; <see cref="Root"/> sits at the bottom for the whole parse.</summary>
    private Stack<OwlFunctionalNode> Open { get; } = new();

    /// <summary>The open top-level <c>Ontology(…)</c> group whose children stream to the converter, or <see langword="null"/>.</summary>
    private OwlFunctionalNode? OntologyGroup { get; set; }

    /// <summary>A name token held back until the next token decides headed group versus bare atom.</summary>
    private OwlFunctionalToken? PendingName { get; set; }

    /// <summary>The exclusive end offset of the last accepted token; unterminated groups span to it.</summary>
    private int LastTokenEnd { get; set; }

    /// <summary>Initialises an empty reader; feed source bytes through <see cref="Feed"/>.</summary>
    public OwlFunctionalSyntaxIncrementalReader()
    {
        Open.Push(Root);
    }

    /// <summary>
    /// Gets the diagnostics recorded so far. While the input is incomplete the
    /// bag holds only genuine faults — an unfinished tail is reported through
    /// <see cref="Status"/>, never here.
    /// </summary>
    public DiagnosticBag Diagnostics => Converter.Diagnostics;

    /// <summary>
    /// Gets whether the input fed so far ends at a document boundary or
    /// inside an unfinished construct.
    /// </summary>
    public IncrementalParseStatus Status =>
        LexSuspended || PendingName is not null || Open.Count > 1
            ? IncrementalParseStatus.NeedMore
            : IncrementalParseStatus.Complete;

    /// <summary>
    /// Appends source bytes and parses as far as the input now permits.
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
    /// Declares the input final and returns the structural document. From
    /// here on truncation is an error: an unterminated token reports a
    /// lexical diagnostic and every unterminated group reports an unbalanced
    /// parenthesis, while the converted content parsed so far is kept.
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
        FlushPendingName();
        CloseUnterminatedGroups();

        Document = new OwlOntologyDocument(
            Converter.Axioms.ToImmutable(),
            Converter.OntologyIri,
            Converter.Diagnostics,
            Converter.DeclaredClasses,
            Converter.DeclaredObjectProperties,
            Converter.DeclaredDataProperties,
            Converter.DeclaredAnnotationProperties,
            Converter.DeclaredDatatypes);

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
            LexStep step = TryLexToken(out OwlFunctionalToken token);

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

    //The resumable tokenizer core. Whitespace and complete comments commit as
    //they pass; a token commits only when its end is fixed by a delimiter or,
    //in final mode, the end of input. A token whose extent the buffer end
    //would otherwise decide reports NeedMore and is re-lexed from its start
    //once more bytes arrive — so a suspended position is never partially
    //committed. Truncation faults (an unterminated IRI, literal, or datatype
    //IRI) can only occur at the final tail, where the batch behaviour is
    //reproduced: report and drop the partial token.
    private LexStep TryLexToken(out OwlFunctionalToken token)
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
                    //A comment without its newline may still be growing; in
                    //final mode it runs to the end of input and is consumed.
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

            if(c == (byte)'(')
            {
                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Open, default, null, null, i, i + 1);
                LexPosition = i + 1;

                return LexStep.Token;
            }

            if(c == (byte)')')
            {
                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Close, default, null, null, i, i + 1);
                LexPosition = i + 1;

                return LexStep.Token;
            }

            if(c == (byte)'=')
            {
                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Equals, default, null, null, i, i + 1);
                LexPosition = i + 1;

                return LexStep.Token;
            }

            if(c == (byte)'<')
            {
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

                token = new OwlFunctionalToken(OwlFunctionalTokenKind.BlankNode, Slice(start, end), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            if(IsAsciiDigit(c))
            {
                int end = i;
                while(end < text.Length && IsAsciiDigit(text[end]))
                {
                    end++;
                }

                if(end == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Number, Slice(i, end), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
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

                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Name, Slice(i, end), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            Report($"Unexpected character '{(char)c}'.", Map.Span(i, i + 1));
            i++;
            LexPosition = i;
        }
    }

    /// <summary>Lexes a <c>&lt;…&gt;</c> IRI reference starting at <paramref name="start"/>.</summary>
    /// <param name="text">The buffered bytes.</param>
    /// <param name="start">The offset of the opening angle bracket.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexIri(ReadOnlySpan<byte> text, int start, out OwlFunctionalToken token)
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

        token = new OwlFunctionalToken(OwlFunctionalTokenKind.Iri, Slice(start + 1, start + 1 + close), null, null, start, start + close + 2);
        LexPosition = token.End;

        return LexStep.Token;
    }

    /// <summary>Lexes a quoted literal with its optional <c>^^datatype</c> or <c>@language</c> suffix.</summary>
    /// <param name="text">The buffered bytes.</param>
    /// <param name="start">The offset of the opening quote.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexLiteral(ReadOnlySpan<byte> text, int start, out OwlFunctionalToken token)
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

        token = new OwlFunctionalToken(OwlFunctionalTokenKind.Literal, new Utf8String(value.AsMemory(0, valueLength)), datatype, language, start, i);
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
    /// <returns><see langword="true"/> when the byte may start a constructor name or prefixed name.</returns>
    private static bool IsNameStartByte(byte b)
    {
        return ((b | 0x20) >= (byte)'a' && (b | 0x20) <= (byte)'z') || b == (byte)'_' || b >= 0x80;
    }

    /// <summary>Whether a byte can continue a name: an ASCII letter or digit, <c>_</c>, <c>-</c>, <c>.</c>, or any non-ASCII byte.</summary>
    /// <param name="b">The byte to classify.</param>
    /// <returns><see langword="true"/> when the byte may continue a constructor name or prefixed name.</returns>
    private static bool IsNameByte(byte b)
    {
        return IsAsciiLetterOrDigit(b) || b == (byte)'_' || b == (byte)'-' || b == (byte)'.' || b >= 0x80;
    }

    //The streaming tree builder. A name token is held back one step because
    //only the following token decides whether it heads a constructor group;
    //everything else lands in the tree immediately.
    private void Accept(OwlFunctionalToken token)
    {
        LastTokenEnd = token.End;

        if(PendingName is OwlFunctionalToken head)
        {
            PendingName = null;

            if(token.Kind == OwlFunctionalTokenKind.Open)
            {
                OpenGroup(head.Text, head.Start);

                return;
            }

            AcceptAtom(head);
        }

        if(token.Kind == OwlFunctionalTokenKind.Name)
        {
            PendingName = token;

            return;
        }

        if(token.Kind == OwlFunctionalTokenKind.Open)
        {
            OpenGroup(null, token.Start);

            return;
        }

        if(token.Kind == OwlFunctionalTokenKind.Close)
        {
            CloseGroup(token);

            return;
        }

        AcceptAtom(token);
    }

    /// <summary>Opens a constructor group and pushes it onto the open stack.</summary>
    /// <param name="head">The constructor name, or <see langword="null"/> for a bare group.</param>
    /// <param name="start">The byte offset the group's span starts at.</param>
    private void OpenGroup(Utf8String? head, int start)
    {
        OwlFunctionalNode group = new() { Head = head, SpanStart = start };
        Open.Peek().Children.Add(group);
        Open.Push(group);

        if(head is Utf8String headValue && OwlFunctionalKeywords.IsOntology(headValue) && Open.Count == 2)
        {
            OntologyGroup = group;
            Converter.BeginOntology();
        }
    }

    /// <summary>Closes the innermost open group and dispatches it for conversion.</summary>
    /// <param name="token">The closing parenthesis token.</param>
    private void CloseGroup(OwlFunctionalToken token)
    {
        if(Open.Count == 1)
        {
            Report("Unbalanced ')'.", Map.Span(token.Start, token.End));

            return;
        }

        OwlFunctionalNode closed = Open.Pop();
        closed.Span = Map.Span(closed.SpanStart, token.End);
        Dispatch(closed);
    }

    //Routes a completed group by where it closed: a child of the streaming
    //Ontology group converts and releases immediately; a top-level group
    //registers (Prefix) or has already streamed (Ontology); a group nested
    //inside an axiom stays attached for its parent's conversion. A closed
    //group is always the last child of its parent — nothing can append to the
    //parent while the child is open — so release pops from the children tail.
    private void Dispatch(OwlFunctionalNode closed)
    {
        OwlFunctionalNode parent = Open.Peek();

        if(ReferenceEquals(closed, OntologyGroup))
        {
            OntologyGroup = null;
            parent.Children.RemoveAt(parent.Children.Count - 1);

            return;
        }

        if(ReferenceEquals(parent, OntologyGroup))
        {
            parent.Children.RemoveAt(parent.Children.Count - 1);
            Converter.AcceptOntologyChild(closed);

            return;
        }

        if(ReferenceEquals(parent, Root))
        {
            parent.Children.RemoveAt(parent.Children.Count - 1);

            if(closed.Head is Utf8String head && OwlFunctionalKeywords.IsPrefix(head))
            {
                Converter.RegisterPrefix(closed);
            }

            //Other top-level groups are not document content; they release unconverted.
        }
    }

    /// <summary>Lands an atom token in the tree, streaming it when it is a direct ontology child.</summary>
    /// <param name="token">The atom token.</param>
    private void AcceptAtom(OwlFunctionalToken token)
    {
        OwlFunctionalNode atom = new() { IsAtom = true, Atom = token, Span = Map.Span(token.Start, token.End) };
        OwlFunctionalNode parent = Open.Peek();

        if(ReferenceEquals(parent, OntologyGroup))
        {
            Converter.AcceptOntologyChild(atom);

            return;
        }

        if(ReferenceEquals(parent, Root))
        {
            //A stray top-level atom is not document content.
            return;
        }

        parent.Children.Add(atom);
    }

    /// <summary>Flushes a held-back name as a bare atom once the input is final.</summary>
    private void FlushPendingName()
    {
        if(PendingName is OwlFunctionalToken head)
        {
            PendingName = null;
            AcceptAtom(head);
        }
    }

    /// <summary>
    /// Closes every group the final input left unterminated, innermost first:
    /// each spans to the last token seen, reports an unbalanced parenthesis,
    /// and still converts with the children gathered so far.
    /// </summary>
    private void CloseUnterminatedGroups()
    {
        while(Open.Count > 1)
        {
            OwlFunctionalNode unterminated = Open.Pop();
            unterminated.Span = Map.Span(unterminated.SpanStart, LastTokenEnd);
            Report("Unbalanced '(' at end of document.", unterminated.Span);
            Dispatch(unterminated);
        }
    }

    /// <summary>Records an error diagnostic into the shared bag.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="span">The source extent the diagnostic covers.</param>
    private void Report(string message, SourceSpan span)
    {
        Converter.Diagnostics.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            span,
            Utf8Strings.From(message)));
    }
}
