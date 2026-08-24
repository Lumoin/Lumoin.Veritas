using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// Bridges a lexer-internal <see cref="SparqlLexDiagnostic"/> to a layer-stable
/// <see cref="Diagnostic"/> carrying a <see cref="WellKnownDiagnostics.Lexer"/> <c>LX####</c> code.
/// </summary>
/// <remarks>
/// This is the single place that knows the <see cref="SparqlLexErrorCode"/> enum. The parser threads
/// the bridged <see cref="Diagnostic"/> values into its parse-level <see cref="DiagnosticBag"/> and
/// never sees the enum. Every lexical error maps to a distinct <c>LX####</c> code, a 1:1 image of the
/// fine-grained <see cref="SparqlLexErrorCode"/> set, so an editor can branch on the code alone. The
/// <see cref="MapCode"/> switch carries no default arm, so adding a <see cref="SparqlLexErrorCode"/>
/// without a matching code is a compile error — the bridge and the catalogue cannot drift apart.
/// </remarks>
internal static class SparqlLexDiagnosticBridge
{
    /// <summary>
    /// Converts a lexical diagnostic to its layer-stable <see cref="Diagnostic"/> form, rendering the
    /// human-readable message once.
    /// </summary>
    /// <param name="diagnostic">The lexer-internal diagnostic.</param>
    /// <returns>An <see cref="DiagnosticSeverity.Error"/>-severity diagnostic with the matching <c>LX####</c> code.</returns>
    public static Diagnostic ToDiagnostic(in SparqlLexDiagnostic diagnostic)
    {
        return new Diagnostic(
            MapCode(diagnostic.Code),
            DiagnosticSeverity.Error,
            diagnostic.Span,
            Utf8Strings.From(diagnostic.GetMessage()));
    }

    /// <summary>
    /// Maps an internal <see cref="SparqlLexErrorCode"/> to its stable <see cref="WellKnownDiagnostics.Lexer"/> code.
    /// </summary>
    /// <param name="code">The internal lexical-error code.</param>
    /// <returns>The matching <c>LX####</c> code.</returns>
    private static Utf8String MapCode(SparqlLexErrorCode code)
    {
        return code switch
        {
            SparqlLexErrorCode.UnterminatedIri => WellKnownDiagnostics.Lexer.UnclosedIri,
            SparqlLexErrorCode.InvalidIriByte => WellKnownDiagnostics.Lexer.InvalidIriByte,
            SparqlLexErrorCode.TruncatedUtf8 => WellKnownDiagnostics.Lexer.TruncatedUtf8,
            SparqlLexErrorCode.InvalidUtf8LeadByte => WellKnownDiagnostics.Lexer.InvalidUtf8LeadByte,
            SparqlLexErrorCode.TruncatedEscape => WellKnownDiagnostics.Lexer.TruncatedEscape,
            SparqlLexErrorCode.InvalidEscape => WellKnownDiagnostics.Lexer.InvalidEscape,
            SparqlLexErrorCode.InvalidHexDigit => WellKnownDiagnostics.Lexer.InvalidHexDigit,
            SparqlLexErrorCode.SurrogateCodePoint => WellKnownDiagnostics.Lexer.SurrogateCodePoint,
            SparqlLexErrorCode.CodePointOutOfRange => WellKnownDiagnostics.Lexer.CodePointOutOfRange,
            SparqlLexErrorCode.UnterminatedString => WellKnownDiagnostics.Lexer.UnclosedStringLiteral,
            SparqlLexErrorCode.UnescapedLineBreak => WellKnownDiagnostics.Lexer.UnescapedLineBreak,
            SparqlLexErrorCode.UnterminatedLongString => WellKnownDiagnostics.Lexer.UnterminatedLongString,
            SparqlLexErrorCode.ExpectedColonAfterUnderscore => WellKnownDiagnostics.Lexer.ExpectedColonAfterUnderscore,
            SparqlLexErrorCode.ExpectedBlankNodeLabel => WellKnownDiagnostics.Lexer.ExpectedBlankNodeLabel,
            SparqlLexErrorCode.ExpectedVariableName => WellKnownDiagnostics.Lexer.ExpectedVariableName,
            SparqlLexErrorCode.ExpectedIdentifierAfterAt => WellKnownDiagnostics.Lexer.ExpectedIdentifierAfterAt,
            SparqlLexErrorCode.ExpectedDirectionTag => WellKnownDiagnostics.Lexer.ExpectedDirectionTag,
            SparqlLexErrorCode.ExpectedLanguageSubtag => WellKnownDiagnostics.Lexer.ExpectedLanguageSubtag,
            SparqlLexErrorCode.UnexpectedByte => WellKnownDiagnostics.Lexer.UnexpectedByte,
            SparqlLexErrorCode.UnrecognisedIdentifier => WellKnownDiagnostics.Lexer.UnrecognisedIdentifier,
            SparqlLexErrorCode.TruncatedPrefixedNameEscape => WellKnownDiagnostics.Lexer.TruncatedPrefixedNameEscape,
            SparqlLexErrorCode.MalformedPercentEscape => WellKnownDiagnostics.Lexer.MalformedPercentEscape,
            SparqlLexErrorCode.InvalidPrefixedNameEscape => WellKnownDiagnostics.Lexer.InvalidPrefixedNameEscape,
            SparqlLexErrorCode.ExpectedDigit => WellKnownDiagnostics.Lexer.ExpectedDigit,
            SparqlLexErrorCode.ExpectedExponentDigits => WellKnownDiagnostics.Lexer.ExpectedExponentDigits,
            SparqlLexErrorCode.InvalidNumericLiteral => WellKnownDiagnostics.Lexer.InvalidNumericLiteral,
            SparqlLexErrorCode.ExpectedTypeMarker => WellKnownDiagnostics.Lexer.ExpectedTypeMarker,
            SparqlLexErrorCode.ExpectedSecondAmpersand => WellKnownDiagnostics.Lexer.ExpectedSecondAmpersand,
            SparqlLexErrorCode.UnexpectedGreaterThan => WellKnownDiagnostics.Lexer.UnexpectedGreaterThan,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "No LX#### code is mapped for this lexical-error code.")
        };
    }
}
