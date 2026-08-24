using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Jsonata.Ast;

/// <summary>
/// An expression in a parsed JSONata program. The closed record hierarchy is the parser's output;
/// the evaluator interprets it. Every node carries the source extent it was parsed from.
/// </summary>
/// <param name="Span">The source extent of the expression.</param>
/// <remarks>
/// <para>
/// Operator identity is carried as a closed enum (<see cref="BinaryOperator"/> /
/// <see cref="UnaryOperator"/>): the lexer emits one token kind per operator and the parser maps it
/// to the enum at construction, so the evaluator dispatches over an exhaustive set.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</para>
/// </remarks>
public abstract record JsonataExpression(SourceSpan Span);

/// <summary>The kind of value a <see cref="LiteralExpression"/> denotes (the JSON-value leaves).</summary>
/// <remarks>See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</remarks>
public enum JsonataLiteralKind
{
    /// <summary>A numeric literal (an IEEE-754 double): <c>42</c>, <c>1.5</c>, <c>6.02e23</c>.</summary>
    /// <remarks>JSONata number literal. See <see href="https://docs.jsonata.org/simple">the JSONata simple-queries reference</see>.</remarks>
    Number,

    /// <summary>A string literal: <c>"a"</c> or <c>'a'</c>, decoded into UTF-8 bytes.</summary>
    /// <remarks>JSONata string literal. See <see href="https://docs.jsonata.org/simple">the JSONata simple-queries reference</see>.</remarks>
    String,

    /// <summary>The boolean literal <c>true</c> or <c>false</c>.</summary>
    /// <remarks>JSONata boolean literal. See <see href="https://docs.jsonata.org/boolean-functions">the JSONata boolean-functions reference</see>.</remarks>
    Boolean,

    /// <summary>The <c>null</c> literal.</summary>
    /// <remarks>JSONata null literal. See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</remarks>
    Null
}

/// <summary>A JSON-value literal: a number, string, boolean, or null.</summary>
/// <param name="Span">The source extent covering the literal lexeme.</param>
/// <param name="Kind">The literal value kind.</param>
/// <param name="Value">The decoded lexical value: the number text, the unescaped string, <c>true</c>/<c>false</c>, or empty for null.</param>
/// <remarks>See <see href="https://docs.jsonata.org/simple">the JSONata simple-queries reference</see>.</remarks>
[DebuggerDisplay("{Kind} {Value}")]
public sealed record LiteralExpression(SourceSpan Span, JsonataLiteralKind Kind, Utf8String Value) : JsonataExpression(Span);

/// <summary>A bare field reference resolved against the current context value: <c>price</c> or <c>`Product Name`</c>.</summary>
/// <param name="Span">The source extent of the name.</param>
/// <param name="Name">The field name (backticks already stripped by the lexer).</param>
/// <remarks>JSONata field reference. See <see href="https://docs.jsonata.org/simple">the JSONata simple-queries reference</see>.</remarks>
[DebuggerDisplay("{Name}")]
public sealed record NameExpression(SourceSpan Span, Utf8String Name) : JsonataExpression(Span);

/// <summary>
/// A regular-expression literal <c>/pattern/flags</c>: a leaf that evaluates to a first-class regex function
/// value (a compiled .NET regular expression carried beside lambdas in the function-value slot). The pattern
/// and the flags are preserved scalar data — the lexer has already split them from the surrounding slashes —
/// so the evaluator compiles the value once when it reaches this node, surfacing an invalid pattern as a
/// JSONata error rather than a leaked regex-compilation exception.
/// </summary>
/// <param name="Span">The source extent covering the whole <c>/pattern/flags</c> literal.</param>
/// <param name="Pattern">The pattern text as written between the slashes, without the flags.</param>
/// <param name="Flags">The flag letters as written after the closing slash (a subset of <c>i</c>/<c>m</c>/<c>s</c>); empty when none.</param>
/// <remarks>JSONata regular-expression literal. See <see href="https://docs.jsonata.org/regex">the JSONata regular-expressions reference</see>.</remarks>
[DebuggerDisplay("/{Pattern}/{Flags}")]
public sealed record RegexExpression(SourceSpan Span, Utf8String Pattern, Utf8String Flags) : JsonataExpression(Span);

/// <summary>The three JSONata variable forms (mirrors <see cref="Lexer.JsonataTokenKind.Variable"/>).</summary>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
public enum VariableForm
{
    /// <summary>The bare context focus <c>$</c>: the current evaluation context value.</summary>
    /// <remarks>JSONata context-value variable <c>$</c>. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
    ContextFocus,

    /// <summary>The root <c>$$</c>: the top-level input document.</summary>
    /// <remarks>JSONata root variable <c>$$</c>. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
    Root,

    /// <summary>A named variable <c>$name</c>: a bound variable or a built-in / registered function.</summary>
    /// <remarks>JSONata named variable <c>$name</c>. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
    Named
}

/// <summary>A variable reference: the context focus <c>$</c>, the root <c>$$</c>, or a named <c>$name</c>.</summary>
/// <param name="Span">The source extent including the leading <c>$</c>(s).</param>
/// <param name="Form">Which variable form this is.</param>
/// <param name="Name">The name without the leading <c>$</c>; empty for <see cref="VariableForm.ContextFocus"/> and <see cref="VariableForm.Root"/>.</param>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
[DebuggerDisplay("${Name}")]
public sealed record VariableExpression(SourceSpan Span, VariableForm Form, Utf8String Name) : JsonataExpression(Span);

/// <summary>A path / map step <c>source.step</c>: the step is evaluated for each item the source side yields.</summary>
/// <param name="Span">The source extent from the source side through the step.</param>
/// <param name="Source">The expression producing the sequence to map over.</param>
/// <param name="Step">The step applied to each item of <see cref="Source"/>.</param>
/// <remarks>JSONata map / dot operator <c>.</c>. See <see href="https://docs.jsonata.org/path-operators">the JSONata path-operators reference</see>.</remarks>
[DebuggerDisplay("(.)")]
public sealed record MapExpression(SourceSpan Span, JsonataExpression Source, JsonataExpression Step) : JsonataExpression(Span);

/// <summary>A predicate / index application <c>source[filter]</c>: a boolean filter or a numeric index over the source sequence.</summary>
/// <param name="Span">The source extent from the source through the closing <c>]</c>.</param>
/// <param name="Source">The sequence being filtered or indexed.</param>
/// <param name="Filter">The predicate or index expression evaluated per item.</param>
/// <remarks>JSONata predicate / index operator <c>[...]</c>. See <see href="https://docs.jsonata.org/path-operators">the JSONata path-operators reference</see>.</remarks>
[DebuggerDisplay("([])")]
public sealed record PredicateExpression(SourceSpan Span, JsonataExpression Source, JsonataExpression Filter) : JsonataExpression(Span);

/// <summary>The binary operators of the JSONata grammar.</summary>
/// <remarks>See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
public enum BinaryOperator
{
    /// <summary>Addition, <c>+</c>.</summary>
    /// <remarks>JSONata <c>+</c>. See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
    Add,

    /// <summary>Subtraction, <c>-</c>.</summary>
    /// <remarks>JSONata <c>-</c>. See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
    Subtract,

    /// <summary>Multiplication, <c>*</c>.</summary>
    /// <remarks>JSONata <c>*</c>. See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
    Multiply,

    /// <summary>Division, <c>/</c>.</summary>
    /// <remarks>JSONata <c>/</c>. See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
    Divide,

    /// <summary>Remainder, <c>%</c>.</summary>
    /// <remarks>JSONata <c>%</c>. See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
    Modulo,

    /// <summary>String concatenation, <c>&amp;</c>.</summary>
    /// <remarks>JSONata <c>&amp;</c>. See <see href="https://docs.jsonata.org/other-operators">the JSONata other-operators reference</see>.</remarks>
    Concat,

    /// <summary>Equality, <c>=</c>.</summary>
    /// <remarks>JSONata <c>=</c>. See <see href="https://docs.jsonata.org/comparison-operators">the JSONata comparison-operators reference</see>.</remarks>
    Equal,

    /// <summary>Inequality, <c>!=</c>.</summary>
    /// <remarks>JSONata <c>!=</c>. See <see href="https://docs.jsonata.org/comparison-operators">the JSONata comparison-operators reference</see>.</remarks>
    NotEqual,

    /// <summary>Less than, <c>&lt;</c>.</summary>
    /// <remarks>JSONata <c>&lt;</c>. See <see href="https://docs.jsonata.org/comparison-operators">the JSONata comparison-operators reference</see>.</remarks>
    Less,

    /// <summary>Less than or equal, <c>&lt;=</c>.</summary>
    /// <remarks>JSONata <c>&lt;=</c>. See <see href="https://docs.jsonata.org/comparison-operators">the JSONata comparison-operators reference</see>.</remarks>
    LessOrEqual,

    /// <summary>Greater than, <c>&gt;</c>.</summary>
    /// <remarks>JSONata <c>&gt;</c>. See <see href="https://docs.jsonata.org/comparison-operators">the JSONata comparison-operators reference</see>.</remarks>
    Greater,

    /// <summary>Greater than or equal, <c>&gt;=</c>.</summary>
    /// <remarks>JSONata <c>&gt;=</c>. See <see href="https://docs.jsonata.org/comparison-operators">the JSONata comparison-operators reference</see>.</remarks>
    GreaterOrEqual,

    /// <summary>Membership, <c>in</c>.</summary>
    /// <remarks>JSONata <c>in</c>. See <see href="https://docs.jsonata.org/comparison-operators">the JSONata comparison-operators reference</see>.</remarks>
    In,

    /// <summary>Logical conjunction, <c>and</c>.</summary>
    /// <remarks>JSONata <c>and</c>. See <see href="https://docs.jsonata.org/boolean-operators">the JSONata boolean-operators reference</see>.</remarks>
    And,

    /// <summary>Logical disjunction, <c>or</c>.</summary>
    /// <remarks>JSONata <c>or</c>. See <see href="https://docs.jsonata.org/boolean-operators">the JSONata boolean-operators reference</see>.</remarks>
    Or
}

/// <summary>A binary expression <c>left op right</c>.</summary>
/// <param name="Span">The source extent from the left operand through the right.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Operator">The binary operator.</param>
/// <param name="Right">The right operand.</param>
/// <remarks>See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
[DebuggerDisplay("({Operator})")]
public sealed record BinaryExpression(SourceSpan Span, JsonataExpression Left, BinaryOperator Operator, JsonataExpression Right) : JsonataExpression(Span);

/// <summary>The unary operators of the JSONata grammar.</summary>
/// <remarks>See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
public enum UnaryOperator
{
    /// <summary>Arithmetic negation, <c>-x</c>.</summary>
    /// <remarks>JSONata unary <c>-</c>. See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
    Negate
}

/// <summary>A unary expression <c>op operand</c> (numeric negation <c>-x</c>).</summary>
/// <param name="Span">The source extent from the operator through the operand.</param>
/// <param name="Operator">The unary operator.</param>
/// <param name="Operand">The operand.</param>
/// <remarks>See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
[DebuggerDisplay("({Operator})")]
public sealed record UnaryExpression(SourceSpan Span, UnaryOperator Operator, JsonataExpression Operand) : JsonataExpression(Span);

/// <summary>
/// The ternary conditional <c>condition ? whenTrue : whenFalse</c>. The else branch is optional in the
/// grammar; the no-else form <c>condition ? whenTrue</c> carries a <see langword="null"/>
/// <see cref="WhenFalse"/>.
/// </summary>
/// <param name="Span">The source extent from the condition through the last branch parsed.</param>
/// <param name="Condition">The condition; its truthiness selects a branch.</param>
/// <param name="WhenTrue">The value when the condition is truthy.</param>
/// <param name="WhenFalse">The value when the condition is falsy, or <see langword="null"/> for the no-else form.</param>
/// <remarks>JSONata conditional operator <c>? :</c>. See <see href="https://docs.jsonata.org/other-operators">the JSONata other-operators reference</see>.</remarks>
[DebuggerDisplay("(?:)")]
public sealed record ConditionalExpression(SourceSpan Span, JsonataExpression Condition, JsonataExpression WhenTrue, JsonataExpression? WhenFalse) : JsonataExpression(Span);

/// <summary>The default-value operators of the JSONata grammar: the falsy-fallback Elvis and the undefined-fallback coalesce.</summary>
/// <remarks>See <see href="https://docs.jsonata.org/other-operators">the JSONata other-operators reference</see>.</remarks>
public enum DefaultOperator
{
    /// <summary>The Elvis operator <c>?:</c>: the left value when its effective boolean value is true, otherwise the right value.</summary>
    /// <remarks>JSONata <c>?:</c>. See <see href="https://docs.jsonata.org/other-operators">the JSONata other-operators reference</see>.</remarks>
    Elvis,

    /// <summary>The coalescing operator <c>??</c>: the left value when it is defined (exists), otherwise the right value.</summary>
    /// <remarks>JSONata <c>??</c>. See <see href="https://docs.jsonata.org/other-operators">the JSONata other-operators reference</see>.</remarks>
    Coalesce
}

/// <summary>
/// A default-value expression <c>left ?: right</c> (Elvis) or <c>left ?? right</c> (coalesce): the left
/// operand is evaluated first and kept when it qualifies (truthy for Elvis, defined for coalesce);
/// otherwise the right operand is evaluated and returned. The right operand is evaluated only on the
/// fallback (short-circuit).
/// </summary>
/// <param name="Span">The source extent from the left operand through the right.</param>
/// <param name="Left">The preferred operand, evaluated first.</param>
/// <param name="Operator">Which default operator: <see cref="DefaultOperator.Elvis"/> or <see cref="DefaultOperator.Coalesce"/>.</param>
/// <param name="Right">The fallback operand, evaluated only when the left does not qualify.</param>
/// <remarks>JSONata default operators <c>?:</c> / <c>??</c>. See <see href="https://docs.jsonata.org/other-operators">the JSONata other-operators reference</see>.</remarks>
[DebuggerDisplay("({Operator})")]
public sealed record DefaultExpression(SourceSpan Span, JsonataExpression Left, DefaultOperator Operator, JsonataExpression Right) : JsonataExpression(Span);

/// <summary>
/// A variable bind <c>$name := value</c>: evaluates <see cref="Value"/> in the current frame, binds it to
/// <see cref="VariableName"/> there, and is itself the bound value. The bind writes into the innermost
/// enclosing block frame (or the top-level program frame when no block encloses it).
/// </summary>
/// <param name="Span">The source extent from the variable through the value.</param>
/// <param name="VariableName">The bound variable's bare name without the leading <c>$</c> (empty for <c>$</c> / <c>$$</c>).</param>
/// <param name="Value">The expression whose value is bound and returned.</param>
/// <remarks>JSONata bind operator <c>:=</c>. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
[DebuggerDisplay("${VariableName} :=")]
public sealed record BindExpression(SourceSpan Span, Utf8String VariableName, JsonataExpression Value) : JsonataExpression(Span);

/// <summary>
/// A parenthesised block <c>( e1 ; e2 ; ... )</c>: evaluates each statement in order in a new variable
/// scope (a child binding frame) and is the last statement's value; an empty block <c>()</c> is undefined.
/// Every parenthesised expression is a block — a single-expression <c>( e )</c> is a one-statement block —
/// so the parentheses both group and frame.
/// </summary>
/// <param name="Span">The source extent from the opening <c>(</c> through the closing <c>)</c>.</param>
/// <param name="Statements">The block's statements in source order; empty for <c>()</c>.</param>
/// <remarks>JSONata block <c>( ... )</c>. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
[DebuggerDisplay("(block)")]
public sealed record BlockExpression(SourceSpan Span, IReadOnlyList<JsonataExpression> Statements) : JsonataExpression(Span);

/// <summary>
/// The wildcard <c>*</c>: selects the values of every field of an object focus, in key order,
/// deep-flattening an array-valued field (arbitrarily nested arrays are spread) into the result. A leaf
/// node — it carries no sub-expression and descends exactly one property level; a non-object focus
/// contributes nothing.
/// </summary>
/// <param name="Span">The source extent covering the <c>*</c>.</param>
/// <remarks>
/// <para>JSONata wildcard <c>*</c>. See <see href="https://docs.jsonata.org/path-operators">the JSONata path-operators reference</see>.</para>
/// <para>
/// This build does not model the upstream length-1 outer-wrapper array focus (which upstream unwraps to
/// its single element before the object scan), so an array focus to <c>*</c> contributes nothing here; the
/// dot/map operator handles per-element application instead. This is a fragment-relative divergence from
/// the reference evaluator.
/// </para>
/// </remarks>
[DebuggerDisplay("(*)")]
public sealed record WildcardExpression(SourceSpan Span) : JsonataExpression(Span);

/// <summary>
/// The descendant <c>**</c>: selects the focus and every value nested below it, at any depth, in
/// pre-order. A leaf node — the recursive descent is an evaluator concern, not a child expression;
/// arrays are transparent containers (only their members are visited, never the array itself).
/// </summary>
/// <param name="Span">The source extent covering the <c>**</c>.</param>
/// <remarks>JSONata descendant <c>**</c>. See <see href="https://docs.jsonata.org/path-operators">the JSONata path-operators reference</see>.</remarks>
[DebuggerDisplay("(**)")]
public sealed record DescendantExpression(SourceSpan Span) : JsonataExpression(Span);

/// <summary>
/// A user-defined function (lambda) definition <c>function($a, $b){ body }</c> (the Greek <c>λ</c> is an
/// alias for <c>function</c>), optionally carrying a bracketed type signature <c>function($a)&lt;n:n&gt;{ body }</c>.
/// The parameters are bare variable names; the body is an arbitrary expression. The function is a
/// first-class value — bindable, passable, returnable, and callable — and at definition the evaluator
/// snapshots the current binding frame and the current focus into the value so a later call evaluates the
/// body against that captured environment.
/// </summary>
/// <param name="Span">The source extent from the <c>function</c> / <c>λ</c> keyword through the closing <c>}</c>.</param>
/// <param name="Parameters">The parameter names in declaration order, each without the leading <c>$</c>; empty for a zero-parameter lambda.</param>
/// <param name="Body">The body expression evaluated when the lambda is applied.</param>
/// <param name="Signature">The bracketed type signature string <c>&lt;params:return&gt;</c> reassembled from the token stream after the parameter list; the empty string (a default <see cref="Utf8String"/>) when the lambda declares no signature. It is preserved scalar data — a leaf datum, not a child expression — that the evaluator parses once into a <see cref="Functions.JsonataSignature"/> when it builds the lambda value.</param>
/// <remarks>JSONata function definition. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
[DebuggerDisplay("(λ)")]
public sealed record LambdaExpression(SourceSpan Span, IReadOnlyList<Utf8String> Parameters, JsonataExpression Body, Utf8String Signature) : JsonataExpression(Span);

/// <summary>
/// A function application <c>procedure(arg0, arg1, ...)</c>: the procedure expression is evaluated to a
/// function value, then the argument expressions are evaluated and bound positionally to the function's
/// parameters before its body runs. Missing trailing arguments bind to undefined; surplus arguments are
/// ignored; invoking a non-function value is an error.
/// </summary>
/// <param name="Span">The source extent from the procedure through the closing <c>)</c>.</param>
/// <param name="Procedure">The expression producing the function value to apply.</param>
/// <param name="Arguments">The argument expressions in source order; empty for a no-argument call.</param>
/// <remarks>JSONata function invocation. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
[DebuggerDisplay("(call)")]
public sealed record CallExpression(SourceSpan Span, JsonataExpression Procedure, IReadOnlyList<JsonataExpression> Arguments) : JsonataExpression(Span);

/// <summary>
/// A partial-application placeholder <c>?</c> standing in an argument position: <c>$f(?, 2)</c> leaves the
/// first argument unbound, partially applying <c>$f</c> with its second argument pre-bound. A leaf node — it
/// carries no sub-expression — that the parser admits only as a <em>leading</em> token in an argument
/// position (a <c>?</c> after an expression remains the ternary <c>? :</c>). The evaluator never evaluates a
/// placeholder to a value: a call or chain whose argument list contains one builds a
/// <see cref="Functions.JsonataPartial"/> rather than invoking the procedure, and the placeholder slots are
/// filled in order when that partial is later applied.
/// </summary>
/// <param name="Span">The source extent covering the <c>?</c>.</param>
/// <remarks>JSONata partial function application. See <see href="https://docs.jsonata.org/programming#partial-function-application">the JSONata programming reference</see>.</remarks>
[DebuggerDisplay("(?)")]
public sealed record PlaceholderExpression(SourceSpan Span) : JsonataExpression(Span);

/// <summary>
/// A numeric range <c>low..high</c>: an ascending integer sequence from <paramref name="Low"/> through
/// <paramref name="High"/> inclusive. An undefined bound yields the empty sequence; a low bound above the
/// high bound yields the empty sequence (the range is never reversed); a defined non-integer bound is an
/// error. The result is a sequence, so a one-element range auto-unwraps to its bare value.
/// </summary>
/// <param name="Span">The source extent from the low bound through the high bound.</param>
/// <param name="Low">The inclusive lower bound expression.</param>
/// <param name="High">The inclusive upper bound expression.</param>
/// <remarks>JSONata range <c>..</c>. See <see href="https://docs.jsonata.org/numeric-operators">the JSONata numeric-operators reference</see>.</remarks>
[DebuggerDisplay("(..)")]
public sealed record RangeExpression(SourceSpan Span, JsonataExpression Low, JsonataExpression High) : JsonataExpression(Span);

/// <summary>
/// A function-application / chain <c>left ~&gt; right</c>: the value of <paramref name="Left"/> is piped
/// into the function on the right. The operator resolves at runtime into one of three forms decided by the
/// shape of <paramref name="Right"/> and the type of the left value: an apply <c>x ~&gt; $f</c> (the right
/// is not a call) evaluates the right to a function and applies it to the left as its single argument; a
/// call-prepend <c>x ~&gt; $f(a, b)</c> (the right is a <see cref="CallExpression"/>) prepends the left as
/// the call's first argument; and a compose <c>$f ~&gt; $g</c> (the left value is itself a function and the
/// right is not a call) builds a new function equivalent to <c>function($x){ $g($f($x)) }</c>.
/// </summary>
/// <param name="Span">The source extent from the left operand through the right.</param>
/// <param name="Left">The left operand: the value piped in, or the first function of a composition.</param>
/// <param name="Right">The right operand: a function to apply, a call to prepend into, or the second function of a composition.</param>
/// <remarks>JSONata function-application / chain operator <c>~&gt;</c>. See <see href="https://docs.jsonata.org/other-operators">the JSONata other-operators reference</see>.</remarks>
[DebuggerDisplay("(~>)")]
public sealed record ApplyExpression(SourceSpan Span, JsonataExpression Left, JsonataExpression Right) : JsonataExpression(Span);

/// <summary>
/// A transform <c>| location | update [, delete] |</c>: a prefix form that evaluates to a first-class
/// function value. Applying that function to an input (typically through the chain operator
/// <c>input ~&gt; | location | update |</c>) deep-clones the input, navigates <see cref="Pattern"/> over the
/// clone to a set of matched nodes, merges the object <see cref="Update"/> evaluates to into each matched
/// object (an undefined update leaves the match unchanged; a defined non-object update is an error), and —
/// when a <see cref="Delete"/> clause is present — removes from each matched object the keys
/// <see cref="Delete"/> evaluates to (a string or array of strings). The original input is left untouched;
/// the modified clone is the result.
/// </summary>
/// <param name="Span">The source extent from the opening <c>|</c> through the closing <c>|</c>.</param>
/// <param name="Pattern">The location expression navigated over the cloned input to the nodes to transform.</param>
/// <param name="Update">The expression, evaluated under each matched node, whose object value is merged into the match.</param>
/// <param name="Delete">The optional expression, evaluated under each matched node, whose string / string-array value names the keys to remove; <see langword="null"/> when the transform has no delete clause.</param>
/// <remarks>JSONata transform operator <c>| ... | ... |</c>. See <see href="https://docs.jsonata.org/other-operators#-------transform">the JSONata transform-operator reference</see>.</remarks>
[DebuggerDisplay("(|...|)")]
public sealed record TransformExpression(SourceSpan Span, JsonataExpression Pattern, JsonataExpression Update, JsonataExpression? Delete) : JsonataExpression(Span);

/// <summary>The sort direction of an order-by term.</summary>
/// <remarks>JSONata order-by <c>^</c>. See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata sorting reference</see>.</remarks>
public enum SortDirection
{
    /// <summary>Ascending order — the default, or an explicit leading <c>&lt;</c>.</summary>
    /// <remarks>JSONata ascending order-by term. See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata sorting reference</see>.</remarks>
    Ascending,

    /// <summary>Descending order — a leading <c>&gt;</c>.</summary>
    /// <remarks>JSONata descending order-by term. See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata sorting reference</see>.</remarks>
    Descending
}

/// <summary>One term of an order-by clause: a key expression and the direction its values are ordered in.</summary>
/// <param name="Direction">The sort direction for this term.</param>
/// <param name="Key">The key expression, evaluated under each element to produce that element's sort key for this term.</param>
/// <remarks>See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata sorting reference</see>.</remarks>
public sealed record SortTerm(SortDirection Direction, JsonataExpression Key);

/// <summary>
/// An order-by <c>source ^ ( term, ... )</c>: sorts the sequence the source produces by one or more terms.
/// Each term's key expression is evaluated under each element; the elements are ordered by the first term,
/// ties broken by the next, and so on, ascending or descending per term. An undefined key sorts last (in
/// either direction); a key that is not a number or string is a T2008 error and two keys of different types
/// are a T2007 error. A source of one value (a non-array) is returned unchanged.
/// </summary>
/// <param name="Span">The source extent from the source through the closing <c>)</c>.</param>
/// <param name="Source">The expression producing the sequence to sort.</param>
/// <param name="Terms">The order-by terms, in priority order; the first is the primary sort key.</param>
/// <remarks>JSONata order-by operator <c>^</c>. See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata sorting reference</see>.</remarks>
[DebuggerDisplay("(^ {Terms.Count})")]
public sealed record SortExpression(SourceSpan Span, JsonataExpression Source, IReadOnlyList<SortTerm> Terms) : JsonataExpression(Span);
