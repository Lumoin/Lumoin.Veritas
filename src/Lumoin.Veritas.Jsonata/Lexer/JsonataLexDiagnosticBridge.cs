using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Jsonata.Lexer;

/// <summary>
/// Bridges a lexer-internal <see cref="JsonataLexDiagnostic"/> to a layer-stable
/// <see cref="Diagnostic"/> carrying a <see cref="WellKnownDiagnostics.Lexer"/> <c>LX####</c> code.
/// </summary>
/// <remarks>
/// This is the single place that knows the <see cref="JsonataLexErrorCode"/> enum. A consumer threads
/// the bridged <see cref="Diagnostic"/> values into its parse-level <see cref="DiagnosticBag"/> and
/// never sees the enum. The lexer reuses the shared <c>LX</c> lexer-code group — the same catalogue
/// the Turtle and SPARQL lexers consume — and adds an <c>LX</c> code only for a lexical condition with
/// no existing equivalent. The <see cref="MapCode"/> switch carries no default arm, so adding a
/// <see cref="JsonataLexErrorCode"/> without a matching code is a compile error — the bridge and the
/// catalogue cannot drift apart.
/// </remarks>
internal static class JsonataLexDiagnosticBridge
{
    /// <summary>
    /// Converts a lexical diagnostic to its layer-stable <see cref="Diagnostic"/> form, rendering the
    /// human-readable message once.
    /// </summary>
    /// <param name="diagnostic">The lexer-internal diagnostic.</param>
    /// <returns>An <see cref="DiagnosticSeverity.Error"/>-severity diagnostic with the matching <c>LX####</c> code.</returns>
    public static Diagnostic ToDiagnostic(in JsonataLexDiagnostic diagnostic)
    {
        return new Diagnostic(
            MapCode(diagnostic.Code),
            DiagnosticSeverity.Error,
            diagnostic.Span,
            Utf8Strings.From(diagnostic.GetMessage()));
    }

    /// <summary>
    /// Maps an internal <see cref="JsonataLexErrorCode"/> to its stable <see cref="WellKnownDiagnostics.Lexer"/> code.
    /// </summary>
    /// <param name="code">The internal lexical-error code.</param>
    /// <returns>The matching <c>LX####</c> code.</returns>
    private static Utf8String MapCode(JsonataLexErrorCode code)
    {
        return code switch
        {
            JsonataLexErrorCode.UnterminatedString => WellKnownDiagnostics.Lexer.UnclosedStringLiteral,
            JsonataLexErrorCode.UnterminatedBacktickName => WellKnownDiagnostics.Lexer.UnclosedStringLiteral,
            JsonataLexErrorCode.InvalidEscape => WellKnownDiagnostics.Lexer.InvalidEscape,
            JsonataLexErrorCode.TruncatedEscape => WellKnownDiagnostics.Lexer.TruncatedEscape,
            JsonataLexErrorCode.InvalidHexDigit => WellKnownDiagnostics.Lexer.InvalidHexDigit,
            JsonataLexErrorCode.UnpairedSurrogate => WellKnownDiagnostics.Lexer.SurrogateCodePoint,
            JsonataLexErrorCode.TruncatedUtf8 => WellKnownDiagnostics.Lexer.TruncatedUtf8,
            JsonataLexErrorCode.InvalidUtf8LeadByte => WellKnownDiagnostics.Lexer.InvalidUtf8LeadByte,
            JsonataLexErrorCode.UnterminatedBlockComment => WellKnownDiagnostics.Lexer.UnterminatedBlockComment,
            JsonataLexErrorCode.UnexpectedByte => WellKnownDiagnostics.Lexer.UnexpectedByte,
            JsonataLexErrorCode.BareExclamation => WellKnownDiagnostics.Lexer.UnexpectedByte,
            JsonataLexErrorCode.BareTilde => WellKnownDiagnostics.Lexer.UnexpectedByte,
            JsonataLexErrorCode.EmptyRegex => WellKnownDiagnostics.Lexer.EmptyRegex,
            JsonataLexErrorCode.UnterminatedRegex => WellKnownDiagnostics.Lexer.UnterminatedRegex,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "No LX#### code is mapped for this lexical-error code.")
        };
    }
}
