using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Owl.Syntax;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>
/// Reads an OWL 2 Manchester-syntax document fed incrementally, chunk by
/// chunk.
/// </summary>
/// <remarks>
/// <para>
/// The reader implements the project's editor contract: after each
/// <see cref="Feed"/> the <see cref="Status"/> reports whether the input so
/// far ends where a document may validly end
/// (<see cref="OwlManchesterParseStatus.Complete"/>) or inside an unfinished
/// construct (<see cref="OwlManchesterParseStatus.NeedMore"/>) — a suspended
/// token, an open group, or a trailing token that grammatically requires a
/// continuation, such as a frame or section keyword, a comma, or an infix
/// operator word. Incompleteness is a status, never a diagnostic. Truncation
/// becomes an error only when <see cref="Complete"/> declares the input
/// final, which is how the whole-buffer facade
/// <see cref="OwlManchesterSyntaxReader.Read"/> consumes the same machinery.
/// </para>
/// <para>
/// The tokenizer is resumable: it commits at token boundaries and re-lexes a
/// suspended token once more characters arrive. Frames convert when
/// <see cref="Complete"/> runs, in two passes — frame subjects declare their
/// entities first, then sections convert to axioms — because Manchester
/// expressions are typed by the declaration census (an undeclared property in
/// a restriction reads as an object property). Comments run from <c>#</c> to
/// the end of the line.
/// </para>
/// </remarks>
public sealed class OwlManchesterSyntaxIncrementalReader
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
    private OwlSourceMap Map { get; } = new();

    /// <summary>The bag every lexical, structural, and conversion diagnostic accumulates into.</summary>
    private DiagnosticBag Bag { get; } = new();

    /// <summary>The tree root standing for the document; its direct children are the top-level tokens and groups.</summary>
    private OwlManchesterNode Root { get; } = new();

    /// <summary>The open-group stack; <see cref="Root"/> sits at the bottom for the whole parse.</summary>
    private Stack<OwlManchesterNode> Open { get; } = new();

    /// <summary>The exclusive end offset of the last accepted token; unterminated groups span to it.</summary>
    private int LastTokenEnd { get; set; }

    /// <summary>Initialises an empty reader; feed source text through <see cref="Feed"/>.</summary>
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
    public OwlManchesterParseStatus Status =>
        LexSuspended || Open.Count > 1 || TailNeedsContinuation()
            ? OwlManchesterParseStatus.NeedMore
            : OwlManchesterParseStatus.Complete;

    /// <summary>
    /// Appends source text and lexes as far as the input now permits.
    /// </summary>
    /// <param name="chunk">The next run of document characters, of any length.</param>
    /// <returns>The <see cref="Status"/> after the chunk is consumed.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> has already been called.</exception>
    public OwlManchesterParseStatus Feed(ReadOnlySpan<char> chunk)
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

        /// <summary>The buffer ended mid-token or mid-comment; more characters decide it, so the position holds at the token start.</summary>
        NeedMore,

        /// <summary>The buffer is exhausted at a clean boundary; no token remains.</summary>
        Exhausted
    }

    //The resumable tokenizer core, the same commitment discipline as the
    //functional-syntax reader: whitespace and complete comments commit as they
    //pass; a token commits only when its end is fixed by a delimiter or, in
    //final mode, the end of input; a suspended token re-lexes from its start
    //once more characters arrive. The Manchester-specific corner is '<': it
    //opens an IRI reference unless what follows makes it a facet comparison
    //('<=', or '<' before whitespace, a digit, a sign, or a quote).
    private LexStep TryLexToken(out OwlManchesterToken token)
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
                case ',':
                {
                    return Single(OwlManchesterTokenKind.Comma, ",", i, out token);
                }

                case '(':
                {
                    return Single(OwlManchesterTokenKind.Open, "(", i, out token);
                }

                case ')':
                {
                    return Single(OwlManchesterTokenKind.Close, ")", i, out token);
                }

                case '{':
                {
                    return Single(OwlManchesterTokenKind.OpenBrace, "{", i, out token);
                }

                case '}':
                {
                    return Single(OwlManchesterTokenKind.CloseBrace, "}", i, out token);
                }

                case '[':
                {
                    return Single(OwlManchesterTokenKind.OpenBracket, "[", i, out token);
                }

                case ']':
                {
                    return Single(OwlManchesterTokenKind.CloseBracket, "]", i, out token);
                }

                default:
                {
                    break;
                }
            }

            if(c == '>')
            {
                if(i + 1 == text.Length && !Final)
                {
                    //A '>' at the buffer end may become '>='.
                    return LexStep.NeedMore;
                }

                if(i + 1 < text.Length && text[i + 1] == '=')
                {
                    token = new OwlManchesterToken(OwlManchesterTokenKind.Comparison, ">=", null, null, i, i + 2);
                    LexPosition = i + 2;

                    return LexStep.Token;
                }

                return Single(OwlManchesterTokenKind.Comparison, ">", i, out token);
            }

            if(c == '<')
            {
                if(i + 1 == text.Length && !Final)
                {
                    //A '<' at the buffer end may become '<=' or open an IRI.
                    return LexStep.NeedMore;
                }

                if(i + 1 < text.Length && text[i + 1] == '=')
                {
                    token = new OwlManchesterToken(OwlManchesterTokenKind.Comparison, "<=", null, null, i, i + 2);
                    LexPosition = i + 2;

                    return LexStep.Token;
                }

                if(i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]) || char.IsDigit(text[i + 1])
                    || text[i + 1] is '+' or '-' or '"')
                {
                    return Single(OwlManchesterTokenKind.Comparison, "<", i, out token);
                }

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

                token = new OwlManchesterToken(OwlManchesterTokenKind.BlankNode, text[start..end].ToString(), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            if(c is '+' or '-')
            {
                if(i + 1 == text.Length && !Final)
                {
                    //A sign at the buffer end may begin a number.
                    return LexStep.NeedMore;
                }

                if(i + 1 < text.Length && (char.IsDigit(text[i + 1]) || text[i + 1] == '.'))
                {
                    return LexNumber(text, i, out token);
                }

                Report($"Unexpected character '{c}'.", Map.Span(i, i + 1));
                i++;
                LexPosition = i;

                continue;
            }

            if(char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                return LexNumber(text, i, out token);
            }

            if(c == '.' && i + 1 == text.Length && !Final)
            {
                //A dot at the buffer end may begin '.5'.
                return LexStep.NeedMore;
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

                token = new OwlManchesterToken(OwlManchesterTokenKind.Name, text[i..end].ToString(), null, null, i, end);
                LexPosition = end;

                return LexStep.Token;
            }

            Report($"Unexpected character '{c}'.", Map.Span(i, i + 1));
            i++;
            LexPosition = i;
        }
    }

    /// <summary>Emits a single-character (or fixed-text) token and commits past it.</summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="text">The token text.</param>
    /// <param name="start">The token's start offset.</param>
    /// <param name="token">The emitted token.</param>
    /// <returns>Always <see cref="LexStep.Token"/>.</returns>
    private LexStep Single(OwlManchesterTokenKind kind, string text, int start, out OwlManchesterToken token)
    {
        token = new OwlManchesterToken(kind, text, null, null, start, start + text.Length);
        LexPosition = token.End;

        return LexStep.Token;
    }

    /// <summary>Lexes a <c>&lt;…&gt;</c> IRI reference starting at <paramref name="start"/>.</summary>
    /// <param name="text">The buffered text.</param>
    /// <param name="start">The offset of the opening angle bracket.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexIri(ReadOnlySpan<char> text, int start, out OwlManchesterToken token)
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

        token = new OwlManchesterToken(OwlManchesterTokenKind.Iri, text.Slice(start + 1, close).ToString(), null, null, start, start + close + 2);
        LexPosition = token.End;

        return LexStep.Token;
    }

    /// <summary>Lexes a quoted literal with its optional <c>^^datatype</c> or <c>@language</c> suffix.</summary>
    /// <param name="text">The buffered text.</param>
    /// <param name="start">The offset of the opening quote.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexLiteral(ReadOnlySpan<char> text, int start, out OwlManchesterToken token)
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

        token = new OwlManchesterToken(OwlManchesterTokenKind.Literal, value.ToString(), datatype, language, start, i);
        LexPosition = i;

        return LexStep.Token;
    }

    /// <summary>
    /// Lexes a numeric literal: optional sign, digits with an optional
    /// fraction (or a bare <c>.digits</c> fraction), an optional exponent,
    /// and an optional <c>f</c> suffix. The raw lexical is kept; the
    /// converter infers the datatype from its shape.
    /// </summary>
    /// <param name="text">The buffered text.</param>
    /// <param name="start">The offset the number starts at.</param>
    /// <param name="token">The lexed token on success.</param>
    /// <returns>The step outcome.</returns>
    private LexStep LexNumber(ReadOnlySpan<char> text, int start, out OwlManchesterToken token)
    {
        token = default;

        //The caller guarantees a sign is followed by a digit or a dot.
        int i = start;
        if(text[i] is '+' or '-')
        {
            i++;
        }

        while(i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        if(i < text.Length && text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))
        {
            i++;
            while(i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }
        }
        else if(i < text.Length && text[i] == '.' && i + 1 == text.Length && !Final)
        {
            return LexStep.NeedMore;
        }

        if(i < text.Length && text[i] is 'e' or 'E')
        {
            int exponent = i + 1;
            if(exponent < text.Length && text[exponent] is '+' or '-')
            {
                exponent++;
            }

            if(exponent == text.Length && !Final)
            {
                return LexStep.NeedMore;
            }

            if(exponent < text.Length && char.IsDigit(text[exponent]))
            {
                i = exponent;
                while(i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }
            }
        }

        if(i < text.Length && text[i] is 'f' or 'F')
        {
            i++;
        }

        if(i == text.Length && !Final)
        {
            //More digits, an exponent, or a suffix may still arrive.
            return LexStep.NeedMore;
        }

        token = new OwlManchesterToken(OwlManchesterTokenKind.Number, text[start..i].ToString(), null, null, start, i);
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
            && previous.Atom.Text == OwlManchesterWords.PrefixKeyword;
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
            Utf8String.FromString(message)));
    }
}
