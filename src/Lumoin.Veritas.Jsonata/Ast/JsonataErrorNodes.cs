using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Jsonata.Lexer;

namespace Lumoin.Veritas.Jsonata.Ast;

/// <summary>
/// The placeholder the parser emits during recovery in place of an expression it could not parse. It
/// derives from <see cref="JsonataExpression"/> so it slots into any position the base is expected and
/// the parser's value flow carries it up with no multi-frame unwind. It contributes nothing to
/// evaluation.
/// </summary>
/// <remarks>
/// <para>
/// The contributing diagnostics live in the parse-level
/// <see cref="Lumoin.Veritas.Core.Diagnostics.DiagnosticBag"/> (surfaced via the parse result);
/// <see cref="DiagnosticCodes"/> lists the codes that fired while this node was built and
/// <see cref="SkippedTokens"/> records the tokens the resync logic consumed, for tooling that preserves
/// trivia. <see cref="ExpectedProduction"/> names the grammar production the parser expected — the
/// deliberate data hook for an editor's quick-fix layer.
/// </para>
/// </remarks>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorExpression {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorExpression(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<JsonataToken> SkippedTokens) : JsonataExpression(Span);
