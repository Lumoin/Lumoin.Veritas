using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// Reads an OWL 2 functional-style syntax document fed incrementally,
/// chunk by chunk.
/// </summary>
/// <remarks>
/// <para>
/// The reader implements the project's editor contract: after each
/// <see cref="Feed"/> the <see cref="Status"/> reports whether the input so
/// far ends at a document boundary (<see cref="OwlFunctionalParseStatus.Complete"/>)
/// or inside a token, group, or undecided constructor head
/// (<see cref="OwlFunctionalParseStatus.NeedMore"/>). Incompleteness is a
/// status, never a diagnostic — an editor must not mark a merely-unfinished
/// tail as an error. Truncation becomes an error only when
/// <see cref="Complete"/> declares the input final, which is how the
/// whole-buffer facade <see cref="OwlFunctionalSyntaxReader.Read"/> consumes
/// the same machinery.
/// </para>
/// <para>
/// The core is resumable: the tokenizer commits at token boundaries and
/// re-lexes a suspended token once more characters arrive; the constructor
/// tree keeps its open-group stack between feeds; and each completed child of
/// the <c>Ontology(…)</c> group converts — and releases its subtree — as soon
/// as its closing parenthesis arrives, so peak parse state tracks the largest
/// single axiom rather than the document. All passes run on explicit stacks.
/// </para>
/// </remarks>
public sealed class OwlFunctionalSyntaxIncrementalReader
{
    /// <summary>The document text fed so far.</summary>
    private char[] Buffer { get; set; } = new char[256];

    /// <summary>The number of valid characters in <see cref="Buffer"/>.</summary>
    private int Length { get; set; }

    /// <summary>The committed lex position: the start of the next unlexed content.</summary>
    private int LexPosition { get; set; }

    /// <summary>Whether the last drain suspended mid-token waiting for more characters.</summary>
    private bool LexSuspended { get; set; }

    /// <summary>Whether <see cref="Complete"/> has declared the input final.</summary>
    private bool Final { get; set; }

    /// <summary>The completed document, built once by <see cref="Complete"/>.</summary>
    private OwlOntologyDocument? Document { get; set; }

    /// <summary>Maps character offsets to byte- and line-column source spans.</summary>
    private SourceMap Map { get; } = new();

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

    /// <summary>Initialises an empty reader; feed source text through <see cref="Feed"/>.</summary>
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
    public OwlFunctionalParseStatus Status =>
        LexSuspended || PendingName is not null || Open.Count > 1
            ? OwlFunctionalParseStatus.NeedMore
            : OwlFunctionalParseStatus.Complete;

    /// <summary>
    /// Appends source text and parses as far as the input now permits.
    /// </summary>
    /// <param name="chunk">The next run of document characters, of any length.</param>
    /// <returns>The <see cref="Status"/> after the chunk is consumed.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> has already been called.</exception>
    public OwlFunctionalParseStatus Feed(ReadOnlySpan<char> chunk)
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

    /// <summary>Appends a chunk to the text buffer and extends the source map over it.</summary>
    /// <param name="chunk">The characters to append.</param>
    private void Append(ReadOnlySpan<char> chunk)
    {
        if(Length + chunk.Length > Buffer.Length)
        {
            char[] grown = new char[Math.Max(Buffer.Length * 2, Length + chunk.Length)];
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

        /// <summary>The buffer ended mid-token or mid-comment; more characters decide it, so the position holds at the token start.</summary>
        NeedMore,

        /// <summary>The buffer is exhausted at a clean boundary; no token remains.</summary>
        Exhausted
    }

    //The resumable tokenizer core. Whitespace and complete comments commit as
    //they pass; a token commits only when its end is fixed by a delimiter or,
    //in final mode, the end of input. A token whose extent the buffer end
    //would otherwise decide reports NeedMore and is re-lexed from its start
    //once more characters arrive — so a suspended position is never partially
    //committed. Truncation faults (an unterminated IRI, literal, or datatype
    //IRI) can only occur at the final tail, where the batch behaviour is
    //reproduced: report and drop the partial token.
    private LexStep TryLexToken(out OwlFunctionalToken token)
    {
        token = default;
        ReadOnlySpan<char> text = Buffer.AsSpan(0, Length);
        int i = LexPosition;

        while(true)
        {
            while(i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            LexPosition = i;

            if(i == text.Length)
            {
                return LexStep.Exhausted;
            }

            char c = text[i];

            if(c == '#')
            {
                int newline = text[i..].IndexOf('\n');
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

            if(c == '(')
            {
                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Open, "(", null, null, i, i + 1);
                LexPosition = i + 1;

                return LexStep.Token;
            }

            if(c == ')')
            {
                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Close, ")", null, null, i, i + 1);
                LexPosition = i + 1;

                return LexStep.Token;
            }

            if(c == '=')
            {
                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Equals, "=", null, null, i, i + 1);
                LexPosition = i + 1;

                return LexStep.Token;
            }

            if(c == '<')
            {
                return LexIri(text, i, out token);
            }

            if(c == '"')
            {
                return LexLiteral(text, i, out token);
            }

            if(c == '_' && i + 1 == text.Length && !Final)
            {
                //A lone underscore at the buffer end may become _:label or a name.
                return LexStep.NeedMore;
            }

            if(c == '_' && i + 1 < text.Length && text[i + 1] == ':')
            {
                int start = i + 2;
                int end = start;
                while(end < text.Length && IsNameChar(text[end]))
                {
                    end++;
                }

                if(end == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                token = new OwlFunctionalToken(OwlFunctionalTokenKind.BlankNode, text[start..end].ToString(), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            if(char.IsDigit(c))
            {
                int end = i;
                while(end < text.Length && char.IsDigit(text[end]))
                {
                    end++;
                }

                if(end == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Number, text[i..end].ToString(), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            if(IsNameStartChar(c) || c == ':')
            {
                int end = i;
                while(end < text.Length && (IsNameChar(text[end]) || text[end] == ':'))
                {
                    end++;
                }

                if(end == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                token = new OwlFunctionalToken(OwlFunctionalTokenKind.Name, text[i..end].ToString(), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            Report($"Unexpected character '{c}'.", Map.Span(i, i + 1));
            i++;
            LexPosition = i;
        }
    }

    /// <summary>Lexes a <c>&lt;…&gt;</c> IRI reference starting at <paramref name="start"/>.</summary>
    /// <param name="text">The buffered text.</param>
    /// <param name="start">The offset of the opening angle bracket.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexIri(ReadOnlySpan<char> text, int start, out OwlFunctionalToken token)
    {
        token = default;

        int close = text[(start + 1)..].IndexOf('>');
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

        token = new OwlFunctionalToken(OwlFunctionalTokenKind.Iri, text.Slice(start + 1, close).ToString(), null, null, start, start + close + 2);
        LexPosition = token.End;

        return LexStep.Token;
    }

    /// <summary>Lexes a quoted literal with its optional <c>^^datatype</c> or <c>@language</c> suffix.</summary>
    /// <param name="text">The buffered text.</param>
    /// <param name="start">The offset of the opening quote.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexLiteral(ReadOnlySpan<char> text, int start, out OwlFunctionalToken token)
    {
        token = default;

        StringBuilder value = new();
        int i = start + 1;
        while(i < text.Length && text[i] != '"')
        {
            if(text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                value.Append(text[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => text[i]
                });
            }
            else
            {
                value.Append(text[i]);
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

        string? datatype = null;
        string? language = null;

        if(i + 1 < text.Length && text[i] == '^' && text[i + 1] == '^')
        {
            i += 2;

            if(i == text.Length && !Final)
            {
                return LexStep.NeedMore;
            }

            if(i < text.Length && text[i] == '<')
            {
                int close = text[(i + 1)..].IndexOf('>');
                if(close < 0)
                {
                    if(!Final)
                    {
                        return LexStep.NeedMore;
                    }

                    Report("Unterminated datatype IRI.", Map.Span(i, text.Length));
                    datatype = string.Empty;
                    i = text.Length;
                }
                else
                {
                    datatype = "<" + text.Slice(i + 1, close).ToString();
                    i += close + 2;
                }
            }
            else
            {
                int nameStart = i;
                while(i < text.Length && (IsNameChar(text[i]) || text[i] == ':'))
                {
                    i++;
                }

                if(i == text.Length && !Final)
                {
                    return LexStep.NeedMore;
                }

                datatype = text[nameStart..i].ToString();
            }
        }
        else if(!Final && i + 1 == text.Length && text[i] == '^')
        {
            //A single caret at the buffer end may become the ^^ of a datatype.
            return LexStep.NeedMore;
        }
        else if(i < text.Length && text[i] == '@')
        {
            i++;
            int tagStart = i;
            while(i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '-'))
            {
                i++;
            }

            if(i == text.Length && !Final)
            {
                return LexStep.NeedMore;
            }

            language = text[tagStart..i].ToString();
        }

        token = new OwlFunctionalToken(OwlFunctionalTokenKind.Literal, value.ToString(), datatype, language, start, i);
        LexPosition = i;

        return LexStep.Token;
    }

    private static bool IsNameStartChar(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    private static bool IsNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
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
    /// <param name="start">The character offset the group's span starts at.</param>
    private void OpenGroup(string? head, int start)
    {
        OwlFunctionalNode group = new() { Head = head, SpanStart = start };
        Open.Peek().Children.Add(group);
        Open.Push(group);

        if(head == "Ontology" && Open.Count == 2)
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

            if(closed.Head == "Prefix")
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
            Utf8String.FromString(message)));
    }

    //Maps character offsets of the fed text to source spans: UTF-8 byte
    //offsets via a cumulative-length table, lines and code-point columns via
    //the line-start table. The tables grow per fed chunk, so spans for
    //already-fed text are stable across feeds.
    private sealed class SourceMap
    {
        /// <summary>The cumulative UTF-8 byte length before each character index.</summary>
        private List<int> CumulativeBytes { get; } = [];

        /// <summary>The character index each line starts at.</summary>
        private List<int> LineStarts { get; } = [0];

        /// <summary>The UTF-8 byte length of all text appended so far.</summary>
        private int TotalBytes { get; set; }

        /// <summary>Extends the tables over an appended chunk.</summary>
        /// <param name="chunk">The appended characters.</param>
        public void Append(ReadOnlySpan<char> chunk)
        {
            foreach(char c in chunk)
            {
                CumulativeBytes.Add(TotalBytes);

                //The character's UTF-8 width; each half of a surrogate
                //pair contributes two of the pair's four bytes.
                TotalBytes += c switch
                {
                    <= '\u007F' => 1,
                    <= '\u07FF' => 2,
                    _ when char.IsSurrogate(c) => 2,
                    _ => 3
                };

                if(c == '\n')
                {
                    LineStarts.Add(CumulativeBytes.Count);
                }
            }
        }

        /// <summary>Builds the span for a half-open character range.</summary>
        /// <param name="startChar">The inclusive start character index.</param>
        /// <param name="endChar">The exclusive end character index.</param>
        /// <returns>The span in byte and line-column form.</returns>
        public SourceSpan Span(int startChar, int endChar)
        {
            (int startLine, int startColumn) = Locate(startChar);
            (int endLine, int endColumn) = Locate(endChar);

            return new SourceSpan(ByteAt(startChar), ByteAt(endChar), startLine, startColumn, endLine, endColumn);
        }

        /// <summary>Returns the UTF-8 byte offset of a character offset, the end-of-text offset included.</summary>
        /// <param name="charOffset">The character offset.</param>
        /// <returns>The byte offset.</returns>
        private int ByteAt(int charOffset)
        {
            return charOffset == CumulativeBytes.Count ? TotalBytes : CumulativeBytes[charOffset];
        }

        /// <summary>
        /// Locates a character offset as a zero-based line and column: a
        /// binary search over <see cref="LineStarts"/> for the last line
        /// starting at or before the offset; the column is the distance
        /// from that line's start.
        /// </summary>
        /// <param name="charOffset">The character offset to locate.</param>
        /// <returns>The zero-based line index and column.</returns>
        private (int Line, int Column) Locate(int charOffset)
        {
            int low = 0;
            int high = LineStarts.Count - 1;
            while(low < high)
            {
                int middle = (low + high + 1) / 2;
                if(LineStarts[middle] <= charOffset)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return (low, charOffset - LineStarts[low]);
        }
    }
}
