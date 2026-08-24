using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Jsonata.Lexer;

/// <summary>
/// One lexical token produced by <see cref="JsonataLexer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> carries the decoded, unescaped content of the token where the kind has a
/// meaningful payload: the string literal with its escapes resolved, the variable name without the
/// leading <c>$</c>, the field name without its enclosing backticks, the numeric literal in its
/// original textual form. Punctuation and operator tokens carry the raw lexeme bytes; consumers
/// normally ignore <see cref="Value"/> for these.
/// </para>
/// <para>
/// <see cref="Span"/> always covers the full token including any delimiters — the <c>$</c> of a
/// variable, the backticks of a quoted name, the quotes of a string — so editor consumers can
/// highlight the entire lexeme. The decoded <see cref="Value"/> versus the full-lexeme
/// <see cref="Span"/> is load-bearing: a consumer comparing field names uses <see cref="Value"/>,
/// while an editor underlining the token uses <see cref="Span"/>.
/// </para>
/// </remarks>
/// <param name="Kind">The kind of token.</param>
/// <param name="Span">The position of the token within the source bytes.</param>
/// <param name="Value">The decoded payload, or the raw lexeme for punctuation.</param>
[DebuggerDisplay("{Kind} \"{Value,nq}\" {Span}")]
public readonly record struct JsonataToken(JsonataTokenKind Kind, SourceSpan Span, Utf8String Value);
