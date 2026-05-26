using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// An expression in a <c>FILTER</c>, <c>BIND</c>, <c>HAVING</c>, ORDER BY
/// condition, or SELECT-expression position. The closed hierarchy is the parser's
/// output; the expression evaluator interprets it.
/// </summary>
/// <remarks>
/// Built-in and aggregate function identity is carried as a closed enum
/// (<see cref="BuiltInFunction"/> / <see cref="AggregateFunction"/>): the lexer
/// interns the canonical upper-case name and the parser maps it to the enum at
/// construction (see <see cref="SparqlFunctions"/>), so the evaluator dispatches
/// over an exhaustive set. User-defined and constructor functions are named by IRI
/// via <see cref="FunctionCallExpression"/>.
/// </remarks>
public abstract record ExpressionNode;

/// <summary>A constant RDF term (a literal or IRI used as a value).</summary>
/// <param name="Value">The constant term.</param>
[DebuggerDisplay("{Value}")]
public sealed record ConstantExpression(RdfTerm Value) : ExpressionNode;

/// <summary>A reference to a variable's bound value.</summary>
/// <param name="Variable">The variable.</param>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record VariableExpression(SparqlVariable Variable) : ExpressionNode;

/// <summary>The logical conjunction <c>left &amp;&amp; right</c>.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
[DebuggerDisplay("(&&)")]
public sealed record AndExpression(ExpressionNode Left, ExpressionNode Right) : ExpressionNode;

/// <summary>The logical disjunction <c>left || right</c>.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
[DebuggerDisplay("(||)")]
public sealed record OrExpression(ExpressionNode Left, ExpressionNode Right) : ExpressionNode;

/// <summary>The logical negation <c>!inner</c>.</summary>
/// <param name="Inner">The negated operand.</param>
[DebuggerDisplay("(!)")]
public sealed record NotExpression(ExpressionNode Inner) : ExpressionNode;

/// <summary>The comparison operators of the <c>RelationalExpression</c> production.</summary>
public enum ComparisonOp
{
    /// <summary>Equality, <c>=</c>.</summary>
    Equal,

    /// <summary>Inequality, <c>!=</c>.</summary>
    NotEqual,

    /// <summary>Less than, <c>&lt;</c>.</summary>
    LessThan,

    /// <summary>Less than or equal, <c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary>Greater than, <c>&gt;</c>.</summary>
    GreaterThan,

    /// <summary>Greater than or equal, <c>&gt;=</c>.</summary>
    GreaterOrEqual
}

/// <summary>A relational comparison <c>left op right</c>.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Op">The comparison operator.</param>
/// <param name="Right">The right operand.</param>
[DebuggerDisplay("({Op})")]
public sealed record ComparisonExpression(ExpressionNode Left, ComparisonOp Op, ExpressionNode Right) : ExpressionNode;

/// <summary>The arithmetic operators, including the unary forms.</summary>
public enum ArithmeticOp
{
    /// <summary>Addition, <c>+</c>.</summary>
    Add,

    /// <summary>Subtraction, <c>-</c>.</summary>
    Subtract,

    /// <summary>Multiplication, <c>*</c>.</summary>
    Multiply,

    /// <summary>Division, <c>/</c>.</summary>
    Divide,

    /// <summary>Unary minus, <c>-x</c>.</summary>
    UnaryMinus,

    /// <summary>Unary plus, <c>+x</c>.</summary>
    UnaryPlus
}

/// <summary>
/// An arithmetic expression <c>left op right</c>. For the unary operators
/// (<see cref="ArithmeticOp.UnaryMinus"/>, <see cref="ArithmeticOp.UnaryPlus"/>)
/// <see cref="Right"/> is <c>null</c> and <see cref="Left"/> is the operand.
/// </summary>
/// <param name="Left">The left operand (the sole operand for unary forms).</param>
/// <param name="Op">The arithmetic operator.</param>
/// <param name="Right">The right operand, or <c>null</c> for unary forms.</param>
[DebuggerDisplay("({Op})")]
public sealed record ArithmeticExpression(ExpressionNode Left, ArithmeticOp Op, ExpressionNode? Right) : ExpressionNode;

/// <summary>The <c>value IN (set)</c> test.</summary>
/// <param name="Value">The value tested for membership.</param>
/// <param name="Set">The candidate set.</param>
[DebuggerDisplay("(IN)")]
public sealed record InExpression(ExpressionNode Value, IReadOnlyList<ExpressionNode> Set) : ExpressionNode;

/// <summary>The <c>value NOT IN (set)</c> test.</summary>
/// <param name="Value">The value tested for non-membership.</param>
/// <param name="Set">The candidate set.</param>
[DebuggerDisplay("(NOT IN)")]
public sealed record NotInExpression(ExpressionNode Value, IReadOnlyList<ExpressionNode> Set) : ExpressionNode;

/// <summary>A call to a named function: a user-defined or constructor function identified by IRI.</summary>
/// <param name="Function">The function IRI.</param>
/// <param name="Arguments">The argument expressions.</param>
[DebuggerDisplay("<{Function.Value}>(...)")]
public sealed record FunctionCallExpression(IriRef Function, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;

/// <summary>
/// A call to a reserved built-in function, identified by <see cref="BuiltInFunction"/>.
/// <see cref="IsDistinct"/> carries the <c>DISTINCT</c> flag where the grammar permits
/// it. <c>BOUND</c>, <c>IF</c>, and <c>COALESCE</c> have dedicated nodes and never
/// appear here.
/// </summary>
/// <param name="Function">The built-in function.</param>
/// <param name="Arguments">The argument expressions.</param>
/// <param name="IsDistinct">Whether <c>DISTINCT</c> was given.</param>
[DebuggerDisplay("{Function}(...)")]
public sealed record BuiltInCallExpression(BuiltInFunction Function, IReadOnlyList<ExpressionNode> Arguments, bool IsDistinct = false) : ExpressionNode;

/// <summary>The <c>EXISTS { pattern }</c> test.</summary>
/// <param name="Inner">The inner graph pattern.</param>
[DebuggerDisplay("EXISTS")]
public sealed record ExistsExpression(GraphPattern Inner) : ExpressionNode;

/// <summary>The <c>NOT EXISTS { pattern }</c> test.</summary>
/// <param name="Inner">The inner graph pattern.</param>
[DebuggerDisplay("NOT EXISTS")]
public sealed record NotExistsExpression(GraphPattern Inner) : ExpressionNode;

/// <summary>The <c>IF(condition, ifTrue, ifFalse)</c> conditional.</summary>
/// <param name="Condition">The condition expression.</param>
/// <param name="IfTrue">The value when the condition's effective boolean value is true.</param>
/// <param name="IfFalse">The value otherwise.</param>
[DebuggerDisplay("IF")]
public sealed record IfExpression(ExpressionNode Condition, ExpressionNode IfTrue, ExpressionNode IfFalse) : ExpressionNode;

/// <summary>The <c>COALESCE(...)</c> first-non-error expression.</summary>
/// <param name="Alternatives">The candidate expressions, in order.</param>
[DebuggerDisplay("COALESCE")]
public sealed record CoalesceExpression(IReadOnlyList<ExpressionNode> Alternatives) : ExpressionNode;

/// <summary>The <c>BOUND(?var)</c> test.</summary>
/// <param name="Variable">The variable tested for being bound.</param>
[DebuggerDisplay("BOUND(?{Variable.Name})")]
public sealed record BoundExpression(SparqlVariable Variable) : ExpressionNode;

/// <summary>
/// An aggregate expression, appearing in a SELECT expression or a <c>HAVING</c>
/// condition and lifted onto the group operator by the translator.
/// <see cref="IsCountStar"/> marks the <c>COUNT(*)</c> form, where
/// <see cref="Argument"/> is <c>null</c>.
/// </summary>
/// <param name="Function">The aggregate function.</param>
/// <param name="Argument">The aggregated expression, or <c>null</c> for <c>COUNT(*)</c>.</param>
/// <param name="IsDistinct">Whether <c>DISTINCT</c> was given.</param>
/// <param name="IsCountStar">Whether this is the <c>COUNT(*)</c> form.</param>
/// <param name="GroupConcatSeparator">The <c>GROUP_CONCAT</c> separator, or <c>null</c> for the default.</param>
[DebuggerDisplay("{Function}(agg)")]
public sealed record AggregateExpression(
    AggregateFunction Function,
    ExpressionNode? Argument,
    bool IsDistinct,
    bool IsCountStar,
    Utf8String? GroupConcatSeparator) : ExpressionNode;
