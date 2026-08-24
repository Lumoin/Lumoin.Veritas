namespace Lumoin.Veritas.Jsonata.Lexer;

/// <summary>
/// The kind of a <see cref="JsonataToken"/>. Enumerates every terminal the JSONata grammar accepts.
/// </summary>
/// <remarks>
/// <para>
/// The lexer emits one numeric kind for all number literals (JSONata has a single IEEE-754 double
/// number type), one kind for both single- and double-quoted strings, and folds the reserved words
/// <c>and</c>, <c>or</c>, <c>in</c>, and <c>function</c> into dedicated keyword kinds; <c>true</c>,
/// <c>false</c>, and <c>null</c> stay <see cref="Name"/> and are mapped to literals by the parser.
/// </para>
/// <para>
/// <see cref="RegexLiteral"/>, <see cref="SignatureOpen"/>, and <see cref="SignatureClose"/> are
/// defined but never produced by the lexer: a <c>/</c> is only divide or a block-comment
/// start, and <c>&lt;</c>/<c>&gt;</c> are purely comparison operators. The contextual reinterpretation
/// of these forms is a later parser concern.
/// </para>
/// <para>
/// See <see href="https://docs.jsonata.org/">the JSONata language reference</see> for the grammar
/// these terminals belong to.
/// </para>
/// </remarks>
public enum JsonataTokenKind
{
    /// <summary>A numeric literal: <c>42</c>, <c>1.5</c>, <c>6.02e23</c>.</summary>
    Number,

    /// <summary>A single- or double-quoted string literal, decoded into UTF-8 bytes.</summary>
    String,

    /// <summary>A bare field reference or keyword name: <c>price</c>, <c>true</c>, <c>and</c>.</summary>
    Name,

    /// <summary>A backtick-quoted field reference: <c>`Product Name`</c>.</summary>
    BacktickName,

    /// <summary>A variable reference: the bare context focus <c>$</c>, the root <c>$$</c>, or a named <c>$name</c>.</summary>
    Variable,

    /// <summary>A regular-expression literal <c>/pattern/flags</c>. Defined but not produced in increment 1.</summary>
    RegexLiteral,

    /// <summary>The map / field-access operator <c>.</c>.</summary>
    Dot,

    /// <summary>The range operator <c>..</c>.</summary>
    DotDot,

    /// <summary>The predicate / index start <c>[</c>.</summary>
    OpenBracket,

    /// <summary>The predicate / index end <c>]</c>.</summary>
    CloseBracket,

    /// <summary>The object-constructor start <c>{</c>.</summary>
    OpenBrace,

    /// <summary>The object-constructor end <c>}</c>.</summary>
    CloseBrace,

    /// <summary>The grouping / argument-list start <c>(</c>.</summary>
    OpenParen,

    /// <summary>The grouping / argument-list end <c>)</c>.</summary>
    CloseParen,

    /// <summary>The list / argument separator <c>,</c>.</summary>
    Comma,

    /// <summary>The key-value / conditional separator <c>:</c>.</summary>
    Colon,

    /// <summary>The expression separator <c>;</c>.</summary>
    Semicolon,

    /// <summary>The variable-binding operator <c>:=</c>.</summary>
    Assign,

    /// <summary>The addition operator <c>+</c>.</summary>
    Plus,

    /// <summary>The subtraction / unary-negation operator <c>-</c>.</summary>
    Minus,

    /// <summary>The multiplication operator <c>*</c>.</summary>
    Star,

    /// <summary>The division operator <c>/</c>.</summary>
    Slash,

    /// <summary>The remainder operator <c>%</c>.</summary>
    Percent,

    /// <summary>The descendant path operator <c>**</c> (a prefix form; JSONata has no exponentiation operator — exponentiation is the $power function).</summary>
    StarStar,

    /// <summary>The string-concatenation operator <c>&amp;</c>.</summary>
    Ampersand,

    /// <summary>The equality operator <c>=</c>.</summary>
    Equal,

    /// <summary>The inequality operator <c>!=</c>.</summary>
    NotEqual,

    /// <summary>The less-than comparison operator <c>&lt;</c>.</summary>
    Less,

    /// <summary>The less-than-or-equal comparison operator <c>&lt;=</c>.</summary>
    LessEqual,

    /// <summary>The greater-than comparison operator <c>&gt;</c>.</summary>
    Greater,

    /// <summary>The greater-than-or-equal comparison operator <c>&gt;=</c>.</summary>
    GreaterEqual,

    /// <summary>The membership keyword operator <c>in</c>.</summary>
    KeywordIn,

    /// <summary>The logical-and keyword operator <c>and</c>.</summary>
    KeywordAnd,

    /// <summary>The logical-or keyword operator <c>or</c>.</summary>
    KeywordOr,

    /// <summary>The ternary-conditional operator <c>?</c>.</summary>
    Question,

    /// <summary>The Elvis (shortcut-conditional) operator <c>?:</c>.</summary>
    QuestionColon,

    /// <summary>The coalescing operator <c>??</c>.</summary>
    QuestionQuestion,

    /// <summary>The function-chaining operator <c>~&gt;</c>.</summary>
    Chain,

    /// <summary>The sort / order-by operator <c>^</c>.</summary>
    Caret,

    /// <summary>The transform operator <c>|</c>.</summary>
    Pipe,

    /// <summary>The context-variable binding operator <c>@</c>.</summary>
    At,

    /// <summary>The index-variable binding operator <c>#</c>.</summary>
    Hash,

    /// <summary>The function-definition keyword <c>function</c>.</summary>
    KeywordFunction,

    /// <summary>The lambda keyword <c>λ</c> (U+03BB), the single-codepoint alias for <c>function</c>.</summary>
    Lambda,

    /// <summary>The function-signature start <c>&lt;</c> in a signature context. Defined but not produced in increment 1.</summary>
    SignatureOpen,

    /// <summary>The function-signature end <c>&gt;</c> in a signature context. Defined but not produced in increment 1.</summary>
    SignatureClose,

    /// <summary>
    /// A run of bytes the lexer could not tokenise.
    /// </summary>
    /// <remarks>
    /// Recovery emits this token in place of throwing, with a <see cref="JsonataToken.Span"/> covering
    /// the offending bytes; the matching <see cref="JsonataLexDiagnostic"/> is recorded in
    /// <see cref="JsonataLexer.Diagnostics"/>. The parser treats an <see cref="Error"/> token as a
    /// resync point rather than a grammar terminal.
    /// </remarks>
    Error,

    /// <summary>End of the input stream.</summary>
    EndOfInput
}
