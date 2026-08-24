namespace Lumoin.Veritas.Jsonata.Lexer;

/// <summary>
/// Identifies a specific lexical error the <see cref="JsonataLexer"/> can detect.
/// </summary>
/// <remarks>
/// Codes are structured so editor and language-server consumers can categorise a
/// <see cref="JsonataLexDiagnostic"/> — map it to a severity, a quick-fix, or a localised message —
/// without parsing message text. The human-readable message is produced on demand by
/// <see cref="JsonataLexDiagnostic.GetMessage"/>. The enum-to-stable-code mapping lives only in
/// <see cref="JsonataLexDiagnosticBridge"/>.
/// </remarks>
public enum JsonataLexErrorCode
{
    /// <summary>A string literal was opened with a quote but never closed.</summary>
    UnterminatedString,

    /// <summary>A backtick-quoted name was opened but never closed.</summary>
    UnterminatedBacktickName,

    /// <summary>An unrecognised backslash escape sequence was encountered inside a string literal.</summary>
    InvalidEscape,

    /// <summary>An escape sequence was cut short by the end of input.</summary>
    TruncatedEscape,

    /// <summary>A <c>\u</c> escape contained a non-hexadecimal digit.</summary>
    InvalidHexDigit,

    /// <summary>A <c>\u</c> escape decoded to an unpaired UTF-16 surrogate, which is not a Unicode scalar value.</summary>
    UnpairedSurrogate,

    /// <summary>A multi-byte UTF-8 sequence was cut short by the end of input.</summary>
    TruncatedUtf8,

    /// <summary>A byte that cannot begin a UTF-8 sequence was encountered.</summary>
    InvalidUtf8LeadByte,

    /// <summary>A block comment was opened with <c>/*</c> but never closed with <c>*/</c>.</summary>
    UnterminatedBlockComment,

    /// <summary>A byte that cannot begin any token was encountered.</summary>
    UnexpectedByte,

    /// <summary>A lone <c>!</c> was encountered; JSONata has only the <c>!=</c> operator.</summary>
    BareExclamation,

    /// <summary>A lone <c>~</c> was encountered; JSONata has only the <c>~&gt;</c> operator.</summary>
    BareTilde,

    /// <summary>A regular-expression literal had no pattern between its opening and closing slash.</summary>
    EmptyRegex,

    /// <summary>A regular-expression literal was opened with <c>/</c> but never closed before the end of input.</summary>
    UnterminatedRegex
}
