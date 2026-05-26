namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// The kind of a <see cref="SparqlToken"/>. Enumerates every terminal the
/// SPARQL 1.2 Query grammar (§19.8) accepts: term syntax, punctuation and
/// operators, the RDF 1.2 reified-triple and triple-term delimiters, the
/// structural keywords, and the reserved built-in-function and aggregate names.
/// </summary>
/// <remarks>
/// <para>
/// SPARQL keywords are case-insensitive; the lexer canonicalises each to a
/// single token kind regardless of source casing. Built-in function names and
/// aggregate names are recognised as distinct kinds — not as prefixed names —
/// because the grammar treats them as terminals of the <c>BuiltInCall</c> and
/// <c>Aggregate</c> productions, so the parser sees a clean token stream.
/// </para>
/// <para>
/// The boolean literals <c>true</c> / <c>false</c> fold into
/// <see cref="BooleanLiteral"/> (matching the Turtle lexer precedent) rather
/// than carrying their own keyword kinds, and the predicate shorthand
/// <c>a</c> is <see cref="A"/>.
/// </para>
/// </remarks>
public enum SparqlTokenKind
{
    /// <summary>An angle-bracketed IRI reference: <c>&lt;http://example.org/&gt;</c>.</summary>
    Iri,

    /// <summary>A prefixed name: <c>foaf:name</c> or <c>:local</c>.</summary>
    PrefixedName,

    /// <summary>A namespace prefix label in a <c>PREFIX</c> declaration: <c>foaf:</c> or <c>:</c>.</summary>
    PrefixNamespace,

    /// <summary>A blank-node label: <c>_:b0</c>.</summary>
    BlankNodeLabel,

    /// <summary>The anonymous-blank-node sugar <c>[]</c> with only whitespace between the brackets.</summary>
    AnonymousBlankNode,

    /// <summary>A query variable: <c>?name</c> or <c>$name</c>.</summary>
    Variable,

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

    /// <summary>The predicate-position shorthand <c>a</c> for <c>rdf:type</c>.</summary>
    A,

    /// <summary>The statement / triple terminator <c>.</c>.</summary>
    Period,

    /// <summary>The object-list separator <c>,</c>.</summary>
    Comma,

    /// <summary>The predicate-object-list separator <c>;</c>.</summary>
    Semicolon,

    /// <summary>The group / call open parenthesis <c>(</c>.</summary>
    OpenParen,

    /// <summary>The group / call close parenthesis <c>)</c>.</summary>
    CloseParen,

    /// <summary>The blank-node-property-list start <c>[</c>.</summary>
    OpenBracket,

    /// <summary>The blank-node-property-list end <c>]</c>.</summary>
    CloseBracket,

    /// <summary>The group-graph-pattern start <c>{</c>.</summary>
    OpenBrace,

    /// <summary>The group-graph-pattern end <c>}</c>.</summary>
    CloseBrace,

    /// <summary>The multiplication operator / path zero-or-more / <c>SELECT *</c> star <c>*</c>.</summary>
    Star,

    /// <summary>The addition operator / path one-or-more <c>+</c>.</summary>
    Plus,

    /// <summary>The subtraction / unary-minus operator <c>-</c>.</summary>
    Minus,

    /// <summary>The division operator / path-sequence separator <c>/</c>.</summary>
    Slash,

    /// <summary>The path-alternative separator <c>|</c>.</summary>
    Pipe,

    /// <summary>The logical-negation operator / negated-property-set marker <c>!</c>.</summary>
    Bang,

    /// <summary>The path zero-or-one quantifier <c>?</c> (distinct from a variable's leading <c>?</c>).</summary>
    Question,

    /// <summary>The inverse-path operator <c>^</c>.</summary>
    Caret,

    /// <summary>The equality operator <c>=</c>.</summary>
    Equals,

    /// <summary>The inequality operator <c>!=</c>.</summary>
    NotEquals,

    /// <summary>The less-than operator <c>&lt;</c> (disambiguated from an IRI by context).</summary>
    LessThan,

    /// <summary>The less-than-or-equal operator <c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary>The greater-than operator <c>&gt;</c>.</summary>
    GreaterThan,

    /// <summary>The greater-than-or-equal operator <c>&gt;=</c>.</summary>
    GreaterOrEqual,

    /// <summary>The logical-and operator <c>&amp;&amp;</c>.</summary>
    LogicalAnd,

    /// <summary>The logical-or operator <c>||</c>.</summary>
    LogicalOr,

    /// <summary>The reified-triple start <c>&lt;&lt;</c> (RDF 1.2).</summary>
    OpenReifiedTriple,

    /// <summary>The reified-triple end <c>&gt;&gt;</c> (RDF 1.2).</summary>
    CloseReifiedTriple,

    /// <summary>The triple-term start <c>&lt;&lt;(</c> (RDF 1.2).</summary>
    OpenTripleTerm,

    /// <summary>The triple-term end <c>)&gt;&gt;</c> (RDF 1.2).</summary>
    CloseTripleTerm,

    /// <summary>The <c>BASE</c> keyword.</summary>
    BaseKeyword,

    /// <summary>The <c>PREFIX</c> keyword.</summary>
    PrefixKeyword,

    /// <summary>The <c>SELECT</c> keyword.</summary>
    SelectKeyword,

    /// <summary>The <c>CONSTRUCT</c> keyword.</summary>
    ConstructKeyword,

    /// <summary>The <c>ASK</c> keyword.</summary>
    AskKeyword,

    /// <summary>The <c>DESCRIBE</c> keyword.</summary>
    DescribeKeyword,

    /// <summary>The <c>WHERE</c> keyword.</summary>
    WhereKeyword,

    /// <summary>The <c>FROM</c> keyword.</summary>
    FromKeyword,

    /// <summary>The <c>NAMED</c> keyword.</summary>
    NamedKeyword,

    /// <summary>The <c>ORDER</c> keyword.</summary>
    OrderKeyword,

    /// <summary>The <c>BY</c> keyword.</summary>
    ByKeyword,

    /// <summary>The <c>LIMIT</c> keyword.</summary>
    LimitKeyword,

    /// <summary>The <c>OFFSET</c> keyword.</summary>
    OffsetKeyword,

    /// <summary>The <c>DISTINCT</c> keyword.</summary>
    DistinctKeyword,

    /// <summary>The <c>REDUCED</c> keyword.</summary>
    ReducedKeyword,

    /// <summary>The <c>OPTIONAL</c> keyword.</summary>
    OptionalKeyword,

    /// <summary>The <c>UNION</c> keyword.</summary>
    UnionKeyword,

    /// <summary>The <c>MINUS</c> keyword (distinct from the <c>-</c> operator <see cref="Minus"/>).</summary>
    MinusKeyword,

    /// <summary>The <c>FILTER</c> keyword.</summary>
    FilterKeyword,

    /// <summary>The <c>BIND</c> keyword.</summary>
    BindKeyword,

    /// <summary>The <c>AS</c> keyword.</summary>
    AsKeyword,

    /// <summary>The <c>VALUES</c> keyword.</summary>
    ValuesKeyword,

    /// <summary>The <c>UNDEF</c> keyword used inside inline data blocks.</summary>
    UndefKeyword,

    /// <summary>The <c>GROUP</c> keyword.</summary>
    GroupKeyword,

    /// <summary>The <c>HAVING</c> keyword.</summary>
    HavingKeyword,

    /// <summary>The <c>GRAPH</c> keyword.</summary>
    GraphKeyword,

    /// <summary>The <c>SERVICE</c> keyword.</summary>
    ServiceKeyword,

    /// <summary>The <c>SILENT</c> keyword.</summary>
    SilentKeyword,

    /// <summary>The <c>IN</c> keyword.</summary>
    InKeyword,

    /// <summary>The <c>NOT</c> keyword (pairs with <c>IN</c> and <c>EXISTS</c>).</summary>
    NotKeyword,

    /// <summary>The <c>EXISTS</c> keyword.</summary>
    ExistsKeyword,

    /// <summary>The <c>ASC</c> ordering keyword.</summary>
    AscKeyword,

    /// <summary>The <c>DESC</c> ordering keyword.</summary>
    DescKeyword,

    /// <summary>The <c>SEPARATOR</c> keyword used inside <c>GROUP_CONCAT</c>.</summary>
    SeparatorKeyword,

    /// <summary>
    /// A reserved built-in-function name (a <c>BuiltInCall</c> terminal such as
    /// <c>STR</c>, <c>STRLEN</c>, <c>BOUND</c>, <c>IF</c>, <c>COALESCE</c>,
    /// <c>REGEX</c>, <c>IRI</c>, <c>isIRI</c>). The decoded payload carries the
    /// canonical upper-case name so the parser dispatches without re-scanning.
    /// </summary>
    BuiltInFunctionName,

    /// <summary>
    /// A reserved aggregate-function name (<c>COUNT</c>, <c>SUM</c>, <c>MIN</c>,
    /// <c>MAX</c>, <c>AVG</c>, <c>SAMPLE</c>, <c>GROUP_CONCAT</c>). The decoded
    /// payload carries the canonical upper-case name.
    /// </summary>
    AggregateFunctionName,

    /// <summary>End of the input stream.</summary>
    EndOfInput
}
