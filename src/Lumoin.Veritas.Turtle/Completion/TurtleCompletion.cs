using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;

namespace Lumoin.Veritas.Turtle.Completion;

/// <summary>
/// Caret-aware Turtle / TriG completion: given a buffer and a byte offset, produces the
/// <see cref="CompletionContext"/> at that caret — the token kinds the grammar admits next and the open
/// production chain enclosing the caret. The context is built store-free from the lexer and parser.
/// </summary>
public static class TurtleCompletion
{
    /// <summary>
    /// Describes the completion context at <paramref name="caretByteOffset"/> in <paramref name="source"/>.
    /// The text up to the caret is lexed and driven to that point, suspending the parser with its work stack
    /// intact; the innermost open production fixes the expected next tokens, and the open frames give the
    /// enclosing-production chain from outermost to innermost. At a statement boundary, where no frame is open,
    /// the expected tokens are the statement-start set.
    /// </summary>
    /// <param name="source">The UTF-8 Turtle / TriG buffer.</param>
    /// <param name="caretByteOffset">The caret position as a byte offset into <paramref name="source"/>; clamped to the buffer.</param>
    /// <param name="syntax">The syntax flavour to parse as; defaults to <see cref="TurtleSyntax.Turtle"/>.</param>
    /// <param name="pool">The pool token payloads intern into; a private pool is created and disposed when <see langword="null"/>. The returned context exposes only token kinds and production kinds, so no interned value escapes it.</param>
    /// <returns>The completion context at the caret.</returns>
    public static CompletionContext Describe(ReadOnlyMemory<byte> source, int caretByteOffset, TurtleSyntax syntax = TurtleSyntax.Turtle, Utf8StringPool? pool = null)
    {
        int caret = Math.Clamp(caretByteOffset, 0, source.Length);
        ReadOnlyMemory<byte> prefix = source[..caret];

        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        try
        {
            TurtleLexer lexer = new(prefix, effectivePool);
            TurtleParser parser = new(effectivePool, default, syntax);
            TurtleTokenKind lastKind = TurtleTokenKind.EndOfInput;
            TurtleTokenKind secondLastKind = TurtleTokenKind.EndOfInput;
            foreach(TurtleToken token in lexer.Tokenize())
            {
                if(token.Kind != TurtleTokenKind.EndOfInput)
                {
                    secondLastKind = lastKind;
                    lastKind = token.Kind;
                }

                parser.FeedToken(token);
            }

            IReadOnlyList<(ParseFrameKind Kind, int Stage)> openFrames = parser.SuspendOpenFramesAtEndOfInput();

            //An empty stack and a lone top-level Statement frame are both statement boundaries — an incomplete
            //directive or graph block recovers to the latter — so both expect the statement-start set, which in
            //TriG additionally admits a graph block (the GRAPH keyword or an anonymous '{').
            bool atStatementBoundary = openFrames.Count == 0 || openFrames[0].Kind == ParseFrameKind.Statement;
            ImmutableArray<TurtleTokenKind> statementStart = syntax == TurtleSyntax.TriG
                ? TurtleCaretExpectations.TriGStatementStart
                : TurtleCaretExpectations.StatementStart;
            ImmutableArray<TurtleTokenKind> expectedTokens = atStatementBoundary
                ? statementStart
                : TurtleCaretExpectations.ExpectedTokensAt(openFrames[0].Kind, openFrames[0].Stage);

            //An RDF literal's datatype position is invisible to the frame map: the string-literal leaf consumes
            //the '^^' and recovers within one parser step when the input ends there, so the suspended frames
            //report the post-object continuation. The token stream still carries the position exactly — a caret
            //directly after a string literal's '^^' admits precisely an IRI or a prefixed name.
            if(lastKind == TurtleTokenKind.TypeMarker
                && secondLastKind is TurtleTokenKind.StringLiteral or TurtleTokenKind.LongStringLiteral)
            {
                expectedTokens = TurtleCaretExpectations.NamedTermStart;
            }

            //OpenFrames lists the frames innermost-first; EnclosingProductions runs outermost-to-innermost.
            ParseFrameKind[] enclosing = new ParseFrameKind[openFrames.Count];
            for(int i = 0; i < openFrames.Count; i++)
            {
                enclosing[i] = openFrames[openFrames.Count - 1 - i].Kind;
            }

            return new CompletionContext(caret, expectedTokens, enclosing);
        }
        finally
        {
            if(pool is null)
            {
                effectivePool.Dispose();
            }
        }
    }
}
