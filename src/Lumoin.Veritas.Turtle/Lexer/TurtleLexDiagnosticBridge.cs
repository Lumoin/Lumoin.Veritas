using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Turtle.Lexer;

/// <summary>
/// Bridges a lexer-internal <see cref="LexDiagnostic"/> to a layer-stable <see cref="Diagnostic"/>
/// carrying a <see cref="WellKnownDiagnostics.Lexer"/> <c>LX####</c> code.
/// </summary>
/// <remarks>
/// This is the single place that knows the <see cref="TurtleLexErrorCode"/> enum. The reader threads
/// the bridged <see cref="Diagnostic"/> values into its parse-level <see cref="DiagnosticBag"/> and
/// never sees the enum. Every lexical error maps to a distinct <c>LX####</c> code, a 1:1 image of the
/// fine-grained <see cref="TurtleLexErrorCode"/> set, so an editor can branch on the code alone. The
/// <see cref="MapCode"/> switch carries no default arm, so adding a <see cref="TurtleLexErrorCode"/>
/// without a matching code is a compile error — the bridge and the catalogue cannot drift apart. The
/// shared <c>LX</c> catalogue is consumed by both this bridge and the SPARQL one.
/// </remarks>
internal static class TurtleLexDiagnosticBridge
{
    /// <summary>
    /// Converts a lexical diagnostic to its layer-stable <see cref="Diagnostic"/> form, rendering the
    /// human-readable message once.
    /// </summary>
    /// <param name="diagnostic">The lexer-internal diagnostic.</param>
    /// <returns>An <see cref="DiagnosticSeverity.Error"/>-severity diagnostic with the matching <c>LX####</c> code.</returns>
    public static Diagnostic ToDiagnostic(in LexDiagnostic diagnostic)
    {
        return new Diagnostic(
            MapCode(diagnostic.Code),
            DiagnosticSeverity.Error,
            diagnostic.Span,
            Utf8Strings.From(diagnostic.GetMessage()));
    }

    /// <summary>
    /// Maps an internal <see cref="TurtleLexErrorCode"/> to its stable <see cref="WellKnownDiagnostics.Lexer"/> code.
    /// </summary>
    /// <param name="code">The internal lexical-error code.</param>
    /// <returns>The matching <c>LX####</c> code.</returns>
    private static Utf8String MapCode(TurtleLexErrorCode code)
    {
        return code switch
        {
            TurtleLexErrorCode.UnterminatedIri => WellKnownDiagnostics.Lexer.UnclosedIri,
            TurtleLexErrorCode.InvalidIriByte => WellKnownDiagnostics.Lexer.InvalidIriByte,
            TurtleLexErrorCode.TruncatedUtf8 => WellKnownDiagnostics.Lexer.TruncatedUtf8,
            TurtleLexErrorCode.InvalidUtf8LeadByte => WellKnownDiagnostics.Lexer.InvalidUtf8LeadByte,
            TurtleLexErrorCode.TruncatedEscape => WellKnownDiagnostics.Lexer.TruncatedEscape,
            TurtleLexErrorCode.InvalidEscape => WellKnownDiagnostics.Lexer.InvalidEscape,
            TurtleLexErrorCode.InvalidHexDigit => WellKnownDiagnostics.Lexer.InvalidHexDigit,
            TurtleLexErrorCode.SurrogateCodePoint => WellKnownDiagnostics.Lexer.SurrogateCodePoint,
            TurtleLexErrorCode.CodePointOutOfRange => WellKnownDiagnostics.Lexer.CodePointOutOfRange,
            TurtleLexErrorCode.UnterminatedString => WellKnownDiagnostics.Lexer.UnclosedStringLiteral,
            TurtleLexErrorCode.UnescapedLineBreak => WellKnownDiagnostics.Lexer.UnescapedLineBreak,
            TurtleLexErrorCode.UnterminatedLongString => WellKnownDiagnostics.Lexer.UnterminatedLongString,
            TurtleLexErrorCode.ExpectedColonAfterUnderscore => WellKnownDiagnostics.Lexer.ExpectedColonAfterUnderscore,
            TurtleLexErrorCode.ExpectedBlankNodeLabel => WellKnownDiagnostics.Lexer.ExpectedBlankNodeLabel,
            TurtleLexErrorCode.ExpectedIdentifierAfterAt => WellKnownDiagnostics.Lexer.ExpectedIdentifierAfterAt,
            TurtleLexErrorCode.ExpectedDirectionTag => WellKnownDiagnostics.Lexer.ExpectedDirectionTag,
            TurtleLexErrorCode.ExpectedLanguageSubtag => WellKnownDiagnostics.Lexer.ExpectedLanguageSubtag,
            TurtleLexErrorCode.UnexpectedByte => WellKnownDiagnostics.Lexer.UnexpectedByte,
            TurtleLexErrorCode.UnrecognisedIdentifier => WellKnownDiagnostics.Lexer.UnrecognisedIdentifier,
            TurtleLexErrorCode.TruncatedPrefixedNameEscape => WellKnownDiagnostics.Lexer.TruncatedPrefixedNameEscape,
            TurtleLexErrorCode.MalformedPercentEscape => WellKnownDiagnostics.Lexer.MalformedPercentEscape,
            TurtleLexErrorCode.ExpectedDigit => WellKnownDiagnostics.Lexer.ExpectedDigit,
            TurtleLexErrorCode.ExpectedExponentDigits => WellKnownDiagnostics.Lexer.ExpectedExponentDigits,
            TurtleLexErrorCode.InvalidNumericLiteral => WellKnownDiagnostics.Lexer.InvalidNumericLiteral,
            TurtleLexErrorCode.ExpectedTypeMarker => WellKnownDiagnostics.Lexer.ExpectedTypeMarker,
            TurtleLexErrorCode.UnexpectedGreaterThan => WellKnownDiagnostics.Lexer.UnexpectedGreaterThan,
            TurtleLexErrorCode.UnexpectedPipe => WellKnownDiagnostics.Lexer.UnexpectedPipe,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "No LX#### code is mapped for this lexical-error code.")
        };
    }
}
