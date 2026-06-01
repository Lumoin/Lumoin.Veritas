namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// A structured lexical-error code reported by <see cref="SparqlLexer"/> on a
/// <see cref="SparqlLexDiagnostic"/>. The code is stable and machine-readable; the
/// human-readable message is produced on demand by
/// <see cref="SparqlLexDiagnostic.GetMessage"/>.
/// </summary>
public enum SparqlLexErrorCode
{
    /// <summary>An IRI reference was not closed by <c>&gt;</c> before end of input.</summary>
    UnterminatedIri,

    /// <summary>A byte not permitted inside an IRI reference was encountered.</summary>
    InvalidIriByte,

    /// <summary>A multi-byte UTF-8 sequence was cut short by end of input.</summary>
    TruncatedUtf8,

    /// <summary>A byte that is not a valid UTF-8 lead byte was encountered.</summary>
    InvalidUtf8LeadByte,

    /// <summary>An escape sequence was cut short by end of input.</summary>
    TruncatedEscape,

    /// <summary>An unrecognised escape sequence was encountered.</summary>
    InvalidEscape,

    /// <summary>A non-hexadecimal digit appeared in a <c>\u</c> / <c>\U</c> escape.</summary>
    InvalidHexDigit,

    /// <summary>A <c>\u</c> / <c>\U</c> escape named a UTF-16 surrogate code point, which is not a scalar value.</summary>
    SurrogateCodePoint,

    /// <summary>A <c>\U</c> escape named a code point beyond U+10FFFF.</summary>
    CodePointOutOfRange,

    /// <summary>A short string literal was not closed before end of input.</summary>
    UnterminatedString,

    /// <summary>An unescaped line break appeared inside a short string literal.</summary>
    UnescapedLineBreak,

    /// <summary>A long (triple-quoted) string literal was not closed before end of input.</summary>
    UnterminatedLongString,

    /// <summary>A <c>_</c> was not followed by <c>:</c> to begin a blank-node label.</summary>
    ExpectedColonAfterUnderscore,

    /// <summary>A <c>_:</c> was not followed by a valid blank-node label.</summary>
    ExpectedBlankNodeLabel,

    /// <summary>A <c>?</c> or <c>$</c> variable marker was not followed by a valid variable name.</summary>
    ExpectedVariableName,

    /// <summary>An <c>@</c> was not followed by a language-tag identifier.</summary>
    ExpectedIdentifierAfterAt,

    /// <summary>A <c>--</c> direction marker was not followed by a direction tag.</summary>
    ExpectedDirectionTag,

    /// <summary>A <c>-</c> in a language tag was not followed by a subtag.</summary>
    ExpectedLanguageSubtag,

    /// <summary>A byte that begins no valid token was encountered.</summary>
    UnexpectedByte,

    /// <summary>An identifier did not resolve to any keyword, function name, or prefixed name.</summary>
    UnrecognisedIdentifier,

    /// <summary>A reserved-character escape inside a prefixed name was cut short.</summary>
    TruncatedPrefixedNameEscape,

    /// <summary>A percent escape inside a prefixed name was not two hex digits.</summary>
    MalformedPercentEscape,

    /// <summary>A backslash escape inside a prefixed name named a character outside the <c>PN_LOCAL_ESC</c> reserved set.</summary>
    InvalidPrefixedNameEscape,

    /// <summary>A digit was expected (for example after a sign or decimal point) but not found.</summary>
    ExpectedDigit,

    /// <summary>Exponent digits were expected after <c>e</c> / <c>E</c> but not found.</summary>
    ExpectedExponentDigits,

    /// <summary>A numeric literal was malformed.</summary>
    InvalidNumericLiteral,

    /// <summary>A <c>^</c> was expected to form the <c>^^</c> datatype marker but the second <c>^</c> was absent.</summary>
    ExpectedTypeMarker,

    /// <summary>A lone <c>&amp;</c> was encountered; SPARQL has only the <c>&amp;&amp;</c> operator.</summary>
    ExpectedSecondAmpersand,

    /// <summary>A <c>&gt;</c> appeared where no token begins with it (outside <c>&gt;&gt;</c> and <c>&gt;=</c>).</summary>
    UnexpectedGreaterThan
}
