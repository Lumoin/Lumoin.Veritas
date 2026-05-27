namespace Lumoin.Veritas.Turtle.Lexer;

/// <summary>
/// The kind of a <see cref="TurtleToken"/>. Enumerates every terminal
/// the Turtle and TriG grammars accept, including the RDF 1.2
/// additions (triple-term and reified-triple delimiters, the
/// direction-tagged language form, the <c>VERSION</c> directive and
/// annotation block delimiters) and the TriG-only <c>GRAPH</c>
/// keyword and <c>{</c> / <c>}</c> graph-block braces.
/// </summary>
/// <remarks>
/// <para>
/// The lexer emits literals at this granularity so the parser sees a
/// clean token stream — numeric subtype, string-literal long/short
/// form, language vs. dir-language tag — without re-scanning the
/// source.
/// </para>
/// </remarks>
public enum TurtleTokenKind
{
    /// <summary>An angle-bracketed IRI: <c>&lt;http://example.org/&gt;</c>.</summary>
    Iri,

    /// <summary>A prefixed name: <c>foaf:name</c> or <c>:local</c>.</summary>
    PrefixedName,

    /// <summary>A namespace declaration prefix: <c>foaf:</c> or <c>:</c>.</summary>
    PrefixNamespace,

    /// <summary>A blank-node label: <c>_:b0</c>.</summary>
    BlankNodeLabel,

    /// <summary>The anonymous-blank-node sugar: <c>[]</c> with only whitespace between the brackets.</summary>
    AnonymousBlankNode,

    /// <summary>A short (single- or double-quoted) string literal, decoded into UTF-8 bytes.</summary>
    StringLiteral,

    /// <summary>A long (triple-quoted) string literal, decoded into UTF-8 bytes.</summary>
    LongStringLiteral,

    /// <summary>An integer literal: <c>42</c>, <c>-17</c>, <c>+0</c>.</summary>
    IntegerLiteral,

    /// <summary>A decimal literal with a decimal point and no exponent: <c>1.5</c>.</summary>
    DecimalLiteral,

    /// <summary>A double literal with an exponent: <c>1.5e10</c>, <c>.5E-3</c>.</summary>
    DoubleLiteral,

    /// <summary>The boolean keyword <c>true</c> or <c>false</c>.</summary>
    BooleanLiteral,

    /// <summary>A language tag without direction: <c>@en</c>, <c>@en-GB</c>.</summary>
    LangTag,

    /// <summary>A directional language tag: <c>@en--ltr</c>, <c>@en-GB--rtl</c> (RDF 1.2).</summary>
    DirLangTag,

    /// <summary>The literal datatype marker <c>^^</c>.</summary>
    TypeMarker,

    /// <summary>The keyword <c>a</c>: shorthand for <c>rdf:type</c> in the predicate position.</summary>
    A,

    /// <summary>The object-list separator <c>,</c>.</summary>
    Comma,

    /// <summary>The predicate-object-list separator <c>;</c>.</summary>
    Semicolon,

    /// <summary>The statement terminator <c>.</c>.</summary>
    Period,

    /// <summary>The blank-node-property-list start <c>[</c>.</summary>
    OpenBracket,

    /// <summary>The blank-node-property-list end <c>]</c>.</summary>
    CloseBracket,

    /// <summary>The collection start <c>(</c>.</summary>
    OpenParen,

    /// <summary>The collection end <c>)</c>.</summary>
    CloseParen,

    /// <summary>The TriG graph-block start <c>{</c>.</summary>
    OpenBrace,

    /// <summary>The TriG graph-block end <c>}</c>.</summary>
    CloseBrace,

    /// <summary>The reified-triple start <c>&lt;&lt;</c> (RDF 1.2).</summary>
    OpenReifiedTriple,

    /// <summary>The reified-triple end <c>&gt;&gt;</c> (RDF 1.2).</summary>
    CloseReifiedTriple,

    /// <summary>The triple-term start <c>&lt;&lt;(</c> (RDF 1.2).</summary>
    OpenTripleTerm,

    /// <summary>The triple-term end <c>)&gt;&gt;</c> (RDF 1.2).</summary>
    CloseTripleTerm,

    /// <summary>The annotation-block start <c>{|</c> (RDF 1.2).</summary>
    OpenAnnotation,

    /// <summary>The annotation-block end <c>|}</c> (RDF 1.2).</summary>
    CloseAnnotation,

    /// <summary>The reifier marker <c>~</c> (RDF 1.2).</summary>
    Tilde,

    /// <summary>The <c>@prefix</c> or <c>PREFIX</c> keyword.</summary>
    PrefixKeyword,

    /// <summary>The <c>@base</c> or <c>BASE</c> keyword.</summary>
    BaseKeyword,

    /// <summary>The <c>@version</c> or <c>VERSION</c> keyword (RDF 1.2).</summary>
    VersionKeyword,

    /// <summary>The TriG <c>GRAPH</c> keyword.</summary>
    GraphKeyword,

    /// <summary>
    /// A run of bytes the lexer could not tokenise.
    /// </summary>
    /// <remarks>
    /// Recovery emits this token in place of throwing, with a <see cref="TurtleToken.Span"/> covering
    /// the offending bytes; the matching <see cref="LexDiagnostic"/> is recorded in
    /// <see cref="TurtleLexer.Diagnostics"/>. The parser treats an <see cref="Error"/> token as a
    /// resync point rather than a grammar terminal. See
    /// <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar</see>.
    /// </remarks>
    Error,

    /// <summary>End of the input stream.</summary>
    EndOfInput
}
