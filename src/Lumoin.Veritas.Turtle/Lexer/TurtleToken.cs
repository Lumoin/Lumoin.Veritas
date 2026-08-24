using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Lexer;

/// <summary>
/// One lexical token produced by <see cref="TurtleLexer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> carries the decoded, unescaped content of the
/// token where the kind has a meaningful payload: the IRI without its
/// angle brackets, the prefixed-name in <c>pfx:local</c> form, the
/// blank-node label without the <c>_:</c> prefix, the unescaped
/// string-literal bytes, the language tag without the leading <c>@</c>,
/// the numeric literal in its original textual form. Punctuation and
/// keyword tokens carry the raw lexeme bytes; consumers normally
/// ignore <see cref="Value"/> for these.
/// </para>
/// <para>
/// <see cref="Span"/> always covers the full token including any
/// delimiters, so editor consumers can highlight the entire lexeme.
/// </para>
/// </remarks>
/// <param name="Kind">The kind of token.</param>
/// <param name="Span">The position of the token within the source bytes.</param>
/// <param name="Value">The decoded payload, or the raw lexeme for punctuation.</param>
[DebuggerDisplay("{Kind} \"{Value,nq}\" {Span}")]
public readonly record struct TurtleToken(TurtleTokenKind Kind, SourceSpan Span, Utf8String Value);
