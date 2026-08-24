using System.Collections.Generic;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;

namespace Lumoin.Veritas.Turtle.Completion;

/// <summary>
/// The caret-aware completion context for a Turtle / TriG buffer: the token kinds the grammar admits next at
/// the caret and the enclosing-production chain around it. Built store-free from the lexer and parser by
/// <see cref="TurtleCompletion.Describe"/>; an editor renders the expected tokens (as keyword / term / IRI
/// proposals) and uses the production chain for context. Turtle data has no variables, so — unlike the SPARQL
/// completion context — there is no in-scope-variable axis.
/// </summary>
/// <param name="CaretByteOffset">The caret position as a byte offset into the source, clamped to the buffer.</param>
/// <param name="ExpectedTokens">The token kinds the grammar admits at the caret, in suggestion order.</param>
/// <param name="EnclosingProductions">The open productions enclosing the caret, outermost to innermost.</param>
public sealed record CompletionContext(
    int CaretByteOffset,
    IReadOnlyList<TurtleTokenKind> ExpectedTokens,
    IReadOnlyList<ParseFrameKind> EnclosingProductions);
