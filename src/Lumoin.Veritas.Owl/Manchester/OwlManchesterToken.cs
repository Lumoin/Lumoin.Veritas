using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Manchester;

/// <summary>The lexical classes of Manchester-syntax tokens.</summary>
internal enum OwlManchesterTokenKind
{
    /// <summary>A <c>&lt;…&gt;</c> IRI reference; the token text is the IRI without the angle brackets.</summary>
    Iri = 0,

    /// <summary>A word, possibly carrying colons: a keyword such as <c>Class:</c>, an expression word such as <c>some</c>, or a prefixed or simple name. Classified in context.</summary>
    Name = 1,

    /// <summary>A <c>_:label</c> anonymous individual; the token text is the label.</summary>
    BlankNode = 2,

    /// <summary>A quoted literal, with optional datatype or language tag.</summary>
    Literal = 3,

    /// <summary>A numeric literal in integer, decimal, or floating-point lexical form; the text is the raw lexical.</summary>
    Number = 4,

    /// <summary>A list separator comma.</summary>
    Comma = 5,

    /// <summary>An opening parenthesis.</summary>
    Open = 6,

    /// <summary>A closing parenthesis.</summary>
    Close = 7,

    /// <summary>An opening brace of a one-of enumeration.</summary>
    OpenBrace = 8,

    /// <summary>A closing brace.</summary>
    CloseBrace = 9,

    /// <summary>An opening bracket of a facet list.</summary>
    OpenBracket = 10,

    /// <summary>A closing bracket.</summary>
    CloseBracket = 11,

    /// <summary>A facet comparison operator: <c>&lt;=</c>, <c>&lt;</c>, <c>&gt;=</c>, or <c>&gt;</c>; the text is the operator.</summary>
    Comparison = 12
}

/// <summary>One lexed Manchester-syntax token.</summary>
/// <param name="Kind">The token's lexical class.</param>
/// <param name="Text">The token's value text: the IRI body, raw word, label, decoded literal value, numeric lexical, or — for punctuation — empty. Verbatim values are zero-copy windows over the reader's byte buffer; a decoded literal value is its own buffer.</param>
/// <param name="LiteralDatatype">A literal's datatype as a prefixed name or a <c>&lt;</c>-prefixed IRI, or <see langword="null"/>.</param>
/// <param name="LiteralLanguage">A literal's language tag, or <see langword="null"/>.</param>
/// <param name="Start">The inclusive start byte offset in the source.</param>
/// <param name="End">The exclusive end byte offset in the source.</param>
internal readonly record struct OwlManchesterToken(OwlManchesterTokenKind Kind, Utf8String Text, Utf8String? LiteralDatatype, Utf8String? LiteralLanguage, int Start, int End);
