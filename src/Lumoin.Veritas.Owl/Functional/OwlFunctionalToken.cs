using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>The lexical classes of functional-syntax tokens.</summary>
internal enum OwlFunctionalTokenKind
{
    /// <summary>An opening parenthesis.</summary>
    Open = 0,

    /// <summary>A closing parenthesis.</summary>
    Close = 1,

    /// <summary>The <c>=</c> of a prefix declaration.</summary>
    Equals = 2,

    /// <summary>A <c>&lt;…&gt;</c> IRI reference; the token text is the IRI without the angle brackets.</summary>
    Iri = 3,

    /// <summary>A constructor name or prefixed name.</summary>
    Name = 4,

    /// <summary>A <c>_:label</c> anonymous individual; the token text is the label.</summary>
    BlankNode = 5,

    /// <summary>A quoted literal, with optional datatype or language tag.</summary>
    Literal = 6,

    /// <summary>A nonnegative integer, as cardinality bounds use.</summary>
    Number = 7
}

/// <summary>One lexed functional-syntax token.</summary>
/// <param name="Kind">The token's lexical class.</param>
/// <param name="Text">The token's value text: the IRI body, name, label, decoded literal value, or — for punctuation — empty. Verbatim values are zero-copy windows over the reader's byte buffer; a decoded literal value is its own buffer.</param>
/// <param name="LiteralDatatype">A literal's datatype as a prefixed name or a <c>&lt;</c>-prefixed IRI, or <see langword="null"/>.</param>
/// <param name="LiteralLanguage">A literal's language tag, or <see langword="null"/>.</param>
/// <param name="Start">The inclusive start byte offset in the source.</param>
/// <param name="End">The exclusive end byte offset in the source.</param>
internal readonly record struct OwlFunctionalToken(OwlFunctionalTokenKind Kind, Utf8String Text, Utf8String? LiteralDatatype, Utf8String? LiteralLanguage, int Start, int End);
