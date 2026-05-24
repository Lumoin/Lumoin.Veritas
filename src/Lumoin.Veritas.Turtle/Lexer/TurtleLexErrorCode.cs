namespace Lumoin.Veritas.Turtle.Lexer;

/// <summary>
/// Identifies a specific lexical error the <see cref="TurtleLexer"/> can detect.
/// </summary>
/// <remarks>
/// Codes are structured so editor and language-server consumers can categorise a
/// <see cref="LexDiagnostic"/> — map it to a severity, a quick-fix, or a localised message —
/// without parsing message text. The human-readable message is produced on demand by
/// <see cref="LexDiagnostic.GetMessage"/>.
/// </remarks>
public enum TurtleLexErrorCode
{
    /// <summary>An IRI was opened with <c>&lt;</c> but never closed with <c>&gt;</c>.</summary>
    UnterminatedIri,

    /// <summary>A byte not permitted inside an IRI reference appeared between the angle brackets.</summary>
    InvalidIriByte,

    /// <summary>A multi-byte UTF-8 sequence was cut short by the end of input.</summary>
    TruncatedUtf8,

    /// <summary>A byte that cannot begin a UTF-8 sequence was encountered.</summary>
    InvalidUtf8LeadByte,

    /// <summary>An escape sequence was cut short by the end of input.</summary>
    TruncatedEscape,

    /// <summary>An unrecognised escape sequence was encountered.</summary>
    InvalidEscape,

    /// <summary>A <c>\u</c> or <c>\U</c> escape contained a non-hexadecimal digit.</summary>
    InvalidHexDigit,

    /// <summary>A <c>\u</c>/<c>\U</c> escape decoded to a surrogate code point, which is not a Unicode scalar value.</summary>
    SurrogateCodePoint,

    /// <summary>A <c>\U</c> escape decoded to a value beyond the Unicode range.</summary>
    CodePointOutOfRange,

    /// <summary>A short string literal was opened with a quote but never closed.</summary>
    UnterminatedString,

    /// <summary>A raw line break appeared inside a short string literal, which must be escaped.</summary>
    UnescapedLineBreak,

    /// <summary>A long string literal was opened with three quotes but never closed.</summary>
    UnterminatedLongString,

    /// <summary>A <c>_</c> was not followed by the <c>:</c> that begins a blank-node label.</summary>
    ExpectedColonAfterUnderscore,

    /// <summary>A <c>_:</c> was not followed by a valid blank-node label character.</summary>
    ExpectedBlankNodeLabel,

    /// <summary>An <c>@</c> was not followed by a directive keyword or language tag.</summary>
    ExpectedIdentifierAfterAt,

    /// <summary>A direction marker <c>--</c> was not followed by a direction tag.</summary>
    ExpectedDirectionTag,

    /// <summary>A language-tag <c>-</c> was not followed by a subtag.</summary>
    ExpectedLanguageSubtag,

    /// <summary>A byte that cannot begin any token was encountered.</summary>
    UnexpectedByte,

    /// <summary>An identifier did not match any keyword, boolean, or the <c>a</c> shorthand.</summary>
    UnrecognisedIdentifier,

    /// <summary>A reserved-character escape inside a prefixed name was cut short.</summary>
    TruncatedPrefixedNameEscape,

    /// <summary>A percent escape inside a prefixed name was not followed by two hexadecimal digits.</summary>
    MalformedPercentEscape,

    /// <summary>A numeric sign was not followed by a digit or decimal point.</summary>
    ExpectedDigit,

    /// <summary>A numeric exponent marker was not followed by digits.</summary>
    ExpectedExponentDigits,

    /// <summary>A numeric literal had no digits.</summary>
    InvalidNumericLiteral,

    /// <summary>A single <c>^</c> was not followed by the second <c>^</c> of the datatype marker.</summary>
    ExpectedTypeMarker,

    /// <summary>A <c>&gt;</c> appeared where no <c>&gt;&gt;</c> reified-triple close was expected.</summary>
    UnexpectedGreaterThan,

    /// <summary>A <c>|</c> appeared where no <c>|}</c> annotation close was expected.</summary>
    UnexpectedPipe
}
