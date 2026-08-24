using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// One lexical token produced by <see cref="SparqlLexer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> carries the decoded, unescaped content where the kind has
/// a meaningful payload: the IRI without its angle brackets, the prefixed name in
/// <c>pfx:local</c> form, the blank-node label without the <c>_:</c> prefix, the
/// variable name without its leading <c>?</c> / <c>$</c>, the unescaped
/// string-literal bytes, the language tag without the leading <c>@</c>, the
/// numeric literal in its original textual form, and — for
/// <see cref="SparqlTokenKind.BuiltInFunctionName"/> and
/// <see cref="SparqlTokenKind.AggregateFunctionName"/> — the canonical upper-case
/// function name. Punctuation and structural keyword tokens carry the raw lexeme
/// bytes; consumers normally ignore <see cref="Value"/> for those.
/// </para>
/// <para>
/// <see cref="Span"/> always covers the full token including any delimiters, so
/// editor consumers can highlight the entire lexeme.
/// </para>
/// </remarks>
/// <param name="Kind">The kind of token.</param>
/// <param name="Span">The position of the token within the source bytes.</param>
/// <param name="Value">The decoded payload, or the raw lexeme for punctuation.</param>
[DebuggerDisplay("{Kind} \"{Value,nq}\" {Span}")]
public readonly record struct SparqlToken(SparqlTokenKind Kind, SourceSpan Span, Utf8String Value);
