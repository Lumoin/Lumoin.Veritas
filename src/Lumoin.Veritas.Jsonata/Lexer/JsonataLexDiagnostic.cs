using System;
using System.Globalization;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Jsonata.Lexer;

/// <summary>
/// A lexical error reported by the <see cref="JsonataLexer"/>: a structured code, the source span
/// it covers, and an optional detail string carrying specifics (the offending byte, the bad escape
/// character).
/// </summary>
/// <remarks>
/// The diagnostic is the value-based alternative to throwing: an editor or language server collects
/// these, recovers, and keeps lexing; a batch consumer turns one into a thrown
/// <see cref="JsonataParseException"/>. The human-readable message is produced on demand by
/// <see cref="GetMessage"/> so the hot path never builds a string it may not display.
/// </remarks>
/// <param name="Code">The structured error code.</param>
/// <param name="Span">The source span the error covers.</param>
/// <param name="Detail">Optional specifics interpolated into the message, or <see langword="null"/>.</param>
public readonly record struct JsonataLexDiagnostic(JsonataLexErrorCode Code, SourceSpan Span, string? Detail = null)
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
            JsonataLexErrorCode.UnterminatedString => Format($"Unterminated string literal at line {line} column {columnNumber}."),
            JsonataLexErrorCode.UnterminatedBacktickName => Format($"Unterminated backtick-quoted name at line {line} column {columnNumber}."),
            JsonataLexErrorCode.InvalidEscape => Format($"Invalid escape sequence '{detail}' at line {line} column {columnNumber}."),
            JsonataLexErrorCode.TruncatedEscape => Format($"Truncated escape sequence at line {line} column {columnNumber}."),
            JsonataLexErrorCode.InvalidHexDigit => Format($"Invalid hex digit '{detail}' in escape at line {line} column {columnNumber}."),
            JsonataLexErrorCode.UnpairedSurrogate => Format($"Unpaired surrogate {detail} is not a valid Unicode scalar value at line {line} column {columnNumber}."),
            JsonataLexErrorCode.TruncatedUtf8 => Format($"Truncated UTF-8 sequence at line {line} column {columnNumber}."),
            JsonataLexErrorCode.InvalidUtf8LeadByte => Format($"Invalid UTF-8 lead byte {detail} at line {line} column {columnNumber}."),
            JsonataLexErrorCode.UnterminatedBlockComment => Format($"Unterminated block comment at line {line} column {columnNumber}."),
            JsonataLexErrorCode.UnexpectedByte => Format($"Unexpected byte {detail} at line {line} column {columnNumber}."),
            JsonataLexErrorCode.BareExclamation => Format($"Unexpected '!' at line {line} column {columnNumber}; JSONata has only the '!=' operator."),
            JsonataLexErrorCode.BareTilde => Format($"Unexpected '~' at line {line} column {columnNumber}; JSONata has only the '~>' operator."),
            JsonataLexErrorCode.EmptyRegex => Format($"Empty regular-expression literal at line {line} column {columnNumber}."),
            JsonataLexErrorCode.UnterminatedRegex => Format($"Unterminated regular-expression literal at line {line} column {columnNumber}."),
            _ => Format($"Lexical error at line {line} column {columnNumber}.")
        };
    }

    private static string Format(FormattableString message)
    {
        return message.ToString(CultureInfo.InvariantCulture);
    }
}
