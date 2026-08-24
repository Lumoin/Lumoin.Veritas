using Lumoin.Veritas.Jsonata.Lexer;

namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// The canonical JSONata operator binding-power table; higher values bind tighter. The integers mirror
/// the upstream operator-precedence map verbatim so the parser's precedence climbing matches the
/// reference grammar.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Led"/> gives the left binding power of an infix or postfix operator (zero for a token that
/// begins no led). The predicate / index <c>[</c> (80) binds tighter than the map <c>.</c> (75), which
/// changes query meaning: <c>a.b[c=1].d</c> attaches the filter to <c>b</c>. The token <c>**</c> has only
/// a prefix (descendant) form and no led handler: its left binding power is 0, so a <c>**</c> in led
/// position cleanly terminates the precedence climb (there is no exponentiation operator).
/// </para>
/// <para>See <see href="https://docs.jsonata.org/path-operators">the JSONata path-operators reference</see>.</para>
/// </remarks>
internal static class BindingPower
{
    /// <summary>The binding power at which a unary negate parses its operand (binds tighter than every binary operator, looser than the path operators <c>.</c> and <c>[</c>).</summary>
    public const int UnaryOperand = 70;

    /// <summary>The left binding power of the map operator <c>.</c>.</summary>
    public const int Map = 75;

    /// <summary>
    /// The binding power at which the map operator's step sub-expression is parsed. One above
    /// <see cref="Map"/>, so the map operator <c>.</c> is left-associative like every other infix
    /// operator here: a chained <c>.</c> stops the step at the next <c>.</c> and the outer frame keeps the
    /// chain, so <c>a.b.c</c> groups as <c>(a.b).c</c>.
    /// </summary>
    public const int MapStep = Map + 1;

    /// <summary>
    /// Returns the left binding power of an infix or postfix token, or 0 when it begins no led.
    /// </summary>
    /// <param name="kind">The token kind at the cursor.</param>
    /// <returns>The left binding power, or 0.</returns>
    public static int Led(JsonataTokenKind kind) => kind switch
    {
        //The call '(', the filter '[', and the context / positional binds '@' / '#' (deferred) are the
        //bp-80 led operators; the object-group '{' (group-by over a path prefix) is the bp-70 led operator.
        //The separators ',' ';' ':' and the closers ')' ']' '}'
        //carry a binding power in the upstream table but have no led — they terminate an expression — so
        //they return 0 here and end the climb.
        JsonataTokenKind.OpenBracket or JsonataTokenKind.OpenParen
            or JsonataTokenKind.At or JsonataTokenKind.Hash => 80,
        JsonataTokenKind.Dot => Map,
        JsonataTokenKind.OpenBrace => 70,
        JsonataTokenKind.Star or JsonataTokenKind.Slash
            or JsonataTokenKind.Percent => 60,
        JsonataTokenKind.Plus or JsonataTokenKind.Minus or JsonataTokenKind.Ampersand => 50,
        JsonataTokenKind.Equal or JsonataTokenKind.NotEqual or JsonataTokenKind.Less
            or JsonataTokenKind.LessEqual or JsonataTokenKind.Greater or JsonataTokenKind.GreaterEqual
            or JsonataTokenKind.KeywordIn or JsonataTokenKind.Caret or JsonataTokenKind.Chain
            or JsonataTokenKind.QuestionColon or JsonataTokenKind.QuestionQuestion => 40,
        JsonataTokenKind.KeywordAnd => 30,
        JsonataTokenKind.KeywordOr => 25,
        JsonataTokenKind.Question or JsonataTokenKind.DotDot => 20,
        JsonataTokenKind.Assign => 10,
        _ => 0
    };

    /// <summary>
    /// Determines whether a token kind is one of the binary operators this build builds into a
    /// <see cref="Ast.BinaryExpression"/> (arithmetic, modulo, concatenation, comparison, membership,
    /// boolean). The path operators <c>.</c> / <c>[</c> and the conditional <c>?</c> have dedicated led
    /// handlers and are not binary operators.
    /// </summary>
    /// <param name="kind">The token kind at the cursor.</param>
    /// <returns><see langword="true"/> for an in-scope binary operator.</returns>
    public static bool IsBinaryOperator(JsonataTokenKind kind) => kind is JsonataTokenKind.Plus
        or JsonataTokenKind.Minus
        or JsonataTokenKind.Star
        or JsonataTokenKind.Slash
        or JsonataTokenKind.Percent
        or JsonataTokenKind.Ampersand
        or JsonataTokenKind.Equal
        or JsonataTokenKind.NotEqual
        or JsonataTokenKind.Less
        or JsonataTokenKind.LessEqual
        or JsonataTokenKind.Greater
        or JsonataTokenKind.GreaterEqual
        or JsonataTokenKind.KeywordIn
        or JsonataTokenKind.KeywordAnd
        or JsonataTokenKind.KeywordOr;
}
