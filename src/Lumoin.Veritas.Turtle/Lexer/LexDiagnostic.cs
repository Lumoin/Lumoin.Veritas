using System;
using System.Globalization;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Lexer;

/// <summary>
/// A lexical error reported by the <see cref="TurtleLexer"/>: a structured code, the source span
/// it covers, and an optional detail string carrying specifics (the offending byte, the bad escape
/// character, the unrecognised identifier text).
/// </summary>
/// <remarks>
/// The diagnostic is the value-based alternative to throwing: an editor or language server collects
/// these, recovers, and keeps lexing; a batch consumer turns one into a thrown
/// <see cref="TurtleParseException"/>. The human-readable message is produced on demand by
/// <see cref="GetMessage"/> so the hot path never builds a string it may not display.
/// </remarks>
/// <param name="Code">The structured error code.</param>
/// <param name="Span">The source span the error covers.</param>
/// <param name="Detail">Optional specifics interpolated into the message, or <see langword="null"/>.</param>
public readonly record struct LexDiagnostic(TurtleLexErrorCode Code, SourceSpan Span, string? Detail = null)
{
    /// <summary>
    /// Builds the human-readable message for this diagnostic, including its one-based line and
    /// column, from the <see cref="Code"/>, <see cref="Span"/>, and <see cref="Detail"/>.
    /// </summary>
    /// <returns>A descriptive message suitable for a diagnostic surface or exception text.</returns>
    public string GetMessage()
    {
        int line = Span.StartLine + 1;
        int columnNumber = Span.StartColumn + 1;
        string detail = Detail ?? string.Empty;

        return Code switch
        {
            TurtleLexErrorCode.UnterminatedIri => Format($"Unterminated IRI at line {line} column {columnNumber}."),
            TurtleLexErrorCode.InvalidIriByte => Format($"Invalid byte {detail} inside IRI at line {line} column {columnNumber}."),
            TurtleLexErrorCode.TruncatedUtf8 => Format($"Truncated UTF-8 sequence at line {line} column {columnNumber}."),
            TurtleLexErrorCode.InvalidUtf8LeadByte => Format($"Invalid UTF-8 lead byte {detail} at line {line} column {columnNumber}."),
            TurtleLexErrorCode.TruncatedEscape => Format($"Truncated escape sequence at line {line} column {columnNumber}."),
            TurtleLexErrorCode.InvalidEscape => Format($"Invalid escape sequence '{detail}' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.InvalidHexDigit => Format($"Invalid hex digit '{detail}' in escape at line {line} column {columnNumber}."),
            TurtleLexErrorCode.SurrogateCodePoint => Format($"Surrogate code point {detail} is not a valid Unicode scalar value at line {line} column {columnNumber}."),
            TurtleLexErrorCode.CodePointOutOfRange => Format($"Code point {detail} exceeds the Unicode range at line {line} column {columnNumber}."),
            TurtleLexErrorCode.UnterminatedString => Format($"Unterminated string literal at line {line} column {columnNumber}."),
            TurtleLexErrorCode.UnescapedLineBreak => Format($"Unescaped line break inside short string literal at line {line} column {columnNumber}."),
            TurtleLexErrorCode.UnterminatedLongString => Format($"Unterminated long string literal at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedColonAfterUnderscore => Format($"Expected ':' after '_' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedBlankNodeLabel => Format($"Expected blank-node label after '_:' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedIdentifierAfterAt => Format($"Expected identifier after '@' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedDirectionTag => Format($"Expected direction tag after '--' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedLanguageSubtag => Format($"Expected language subtag after '-' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.UnexpectedByte => Format($"Unexpected byte {detail} at line {line} column {columnNumber}."),
            TurtleLexErrorCode.UnrecognisedIdentifier => Format($"Unrecognised identifier '{detail}' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.TruncatedPrefixedNameEscape => Format($"Truncated escape inside prefixed name at line {line} column {columnNumber}."),
            TurtleLexErrorCode.MalformedPercentEscape => Format($"Malformed percent escape inside prefixed name at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedDigit => Format($"Expected digit after '{detail}' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedExponentDigits => Format($"Expected exponent digits at line {line} column {columnNumber}."),
            TurtleLexErrorCode.InvalidNumericLiteral => Format($"Invalid numeric literal at line {line} column {columnNumber}."),
            TurtleLexErrorCode.ExpectedTypeMarker => Format($"Expected '^^' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.UnexpectedGreaterThan => Format($"Unexpected '>' at line {line} column {columnNumber}."),
            TurtleLexErrorCode.UnexpectedPipe => Format($"Unexpected '|' at line {line} column {columnNumber}."),
            _ => Format($"Lexical error at line {line} column {columnNumber}.")
        };
    }

    private static string Format(FormattableString message)
    {
        return message.ToString(CultureInfo.InvariantCulture);
    }
}
