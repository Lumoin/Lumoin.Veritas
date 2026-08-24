using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// An expression in a <c>FILTER</c>, <c>BIND</c>, <c>HAVING</c>, ORDER BY
/// condition, or SELECT-expression position. The closed hierarchy is the parser's
/// output; the expression evaluator interprets it.
/// </summary>
/// <param name="Span">The source extent of the expression.</param>
/// <remarks>
/// <para>
/// Built-in and aggregate function identity is carried as a closed enum
/// (<see cref="BuiltInFunction"/> / <see cref="AggregateFunction"/>): the lexer
/// interns the canonical upper-case name and the parser maps it to the enum at
/// construction (see <see cref="SparqlFunctions"/>), so the evaluator dispatches
/// over an exhaustive set. User-defined and constructor functions are named by IRI
/// via <see cref="FunctionCallExpression"/>.
/// </para>
/// <para>SPARQL <c>Expression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rExpression">SPARQL 1.2 §19.8 [Expression]</see>.</para>
/// </remarks>
public abstract record ExpressionNode(SourceSpan Span);

/// <summary>A constant RDF term (a literal or IRI used as a value).</summary>
/// <param name="Span">The source extent of the constant.</param>
/// <param name="Value">The constant term.</param>
/// <remarks>SPARQL <c>PrimaryExpression</c> (an <c>RDFLiteral</c> / <c>NumericLiteral</c> / <c>iri</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rPrimaryExpression">SPARQL 1.2 §19.8 [PrimaryExpression]</see>.</remarks>
[DebuggerDisplay("{Value}")]
public sealed record ConstantExpression(SourceSpan Span, RdfTerm Value) : ExpressionNode(Span);

/// <summary>A reference to a variable's bound value.</summary>
/// <param name="Span">The source extent of the variable.</param>
/// <param name="Variable">The variable.</param>
/// <remarks>SPARQL <c>Var</c> in <c>PrimaryExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPrimaryExpression">SPARQL 1.2 §19.8 [PrimaryExpression]</see>.</remarks>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record VariableExpression(SourceSpan Span, SparqlVariable Variable) : ExpressionNode(Span);

/// <summary>
/// An RDF 1.2 triple-term expression <c>&lt;&lt;( s verb o )&gt;&gt;</c> in an expression position
/// (for example a <c>BIND</c> right-hand side), denoting the triple term built from its components.
/// Unlike the <see cref="TripleTerm"/> in a triple pattern, the expression form restricts its subject
/// to an IRI or variable (no literal, blank node, or nested triple term) per <c>ExprTripleTerm</c>.
/// </summary>
/// <param name="Span">The source extent from <c>&lt;&lt;(</c> to <c>)&gt;&gt;</c>.</param>
/// <param name="Inner">The denoted triple, whose terms are evaluated as sub-expressions.</param>
/// <remarks>SPARQL <c>ExprTripleTerm</c> in <c>PrimaryExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rExprTripleTerm">SPARQL 1.2 §19.8 [ExprTripleTerm]</see>.</remarks>
[DebuggerDisplay("<<( triple )>>")]
public sealed record TripleTermExpression(SourceSpan Span, TriplePattern Inner) : ExpressionNode(Span);

/// <summary>The logical conjunction <c>left &amp;&amp; right</c>.</summary>
/// <param name="Span">The source extent from the left operand through the right.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
/// <remarks>SPARQL <c>ConditionalAndExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rConditionalAndExpression">SPARQL 1.2 §19.8 [ConditionalAndExpression]</see>.</remarks>
[DebuggerDisplay("(&&)")]
public sealed record AndExpression(SourceSpan Span, ExpressionNode Left, ExpressionNode Right) : ExpressionNode(Span);

/// <summary>The logical disjunction <c>left || right</c>.</summary>
/// <param name="Span">The source extent from the left operand through the right.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
/// <remarks>SPARQL <c>ConditionalOrExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rConditionalOrExpression">SPARQL 1.2 §19.8 [ConditionalOrExpression]</see>.</remarks>
[DebuggerDisplay("(||)")]
public sealed record OrExpression(SourceSpan Span, ExpressionNode Left, ExpressionNode Right) : ExpressionNode(Span);

/// <summary>The logical negation <c>!inner</c>.</summary>
/// <param name="Span">The source extent from the <c>!</c> through the operand.</param>
/// <param name="Inner">The negated operand.</param>
/// <remarks>SPARQL <c>UnaryExpression</c> (<c>'!'</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rUnaryExpression">SPARQL 1.2 §19.8 [UnaryExpression]</see>.</remarks>
[DebuggerDisplay("(!)")]
public sealed record NotExpression(SourceSpan Span, ExpressionNode Inner) : ExpressionNode(Span);

/// <summary>The comparison operators of the <c>RelationalExpression</c> production.</summary>
/// <remarks>SPARQL <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
public enum ComparisonOp
{
    /// <summary>Equality, <c>=</c>.</summary>
    /// <remarks>SPARQL <c>'='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    Equal,

    /// <summary>Inequality, <c>!=</c>.</summary>
    /// <remarks>SPARQL <c>'!='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    NotEqual,

    /// <summary>Less than, <c>&lt;</c>.</summary>
    /// <remarks>SPARQL <c>'&lt;'</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    LessThan,

    /// <summary>Less than or equal, <c>&lt;=</c>.</summary>
    /// <remarks>SPARQL <c>'&lt;='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    LessOrEqual,

    /// <summary>Greater than, <c>&gt;</c>.</summary>
    /// <remarks>SPARQL <c>'&gt;'</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    GreaterThan,

    /// <summary>Greater than or equal, <c>&gt;=</c>.</summary>
    /// <remarks>SPARQL <c>'&gt;='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    GreaterOrEqual
}

/// <summary>A relational comparison <c>left op right</c>.</summary>
/// <param name="Span">The source extent from the left operand through the right.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Op">The comparison operator.</param>
/// <param name="Right">The right operand.</param>
/// <remarks>SPARQL <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
[DebuggerDisplay("({Op})")]
public sealed record ComparisonExpression(SourceSpan Span, ExpressionNode Left, ComparisonOp Op, ExpressionNode Right) : ExpressionNode(Span);

/// <summary>The arithmetic operators, including the unary forms.</summary>
/// <remarks>SPARQL <c>AdditiveExpression</c> / <c>MultiplicativeExpression</c> / <c>UnaryExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAdditiveExpression">SPARQL 1.2 §19.8 [AdditiveExpression]</see>.</remarks>
public enum ArithmeticOp
{
    /// <summary>Addition, <c>+</c>.</summary>
    /// <remarks>SPARQL <c>'+'</c> of <c>AdditiveExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAdditiveExpression">SPARQL 1.2 §19.8 [AdditiveExpression]</see>.</remarks>
    Add,

    /// <summary>Subtraction, <c>-</c>.</summary>
    /// <remarks>SPARQL <c>'-'</c> of <c>AdditiveExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAdditiveExpression">SPARQL 1.2 §19.8 [AdditiveExpression]</see>.</remarks>
    Subtract,

    /// <summary>Multiplication, <c>*</c>.</summary>
    /// <remarks>SPARQL <c>'*'</c> of <c>MultiplicativeExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rMultiplicativeExpression">SPARQL 1.2 §19.8 [MultiplicativeExpression]</see>.</remarks>
    Multiply,

    /// <summary>Division, <c>/</c>.</summary>
    /// <remarks>SPARQL <c>'/'</c> of <c>MultiplicativeExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rMultiplicativeExpression">SPARQL 1.2 §19.8 [MultiplicativeExpression]</see>.</remarks>
    Divide,

    /// <summary>Unary minus, <c>-x</c>.</summary>
    /// <remarks>SPARQL <c>'-'</c> of <c>UnaryExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rUnaryExpression">SPARQL 1.2 §19.8 [UnaryExpression]</see>.</remarks>
    UnaryMinus,

    /// <summary>Unary plus, <c>+x</c>.</summary>
    /// <remarks>SPARQL <c>'+'</c> of <c>UnaryExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rUnaryExpression">SPARQL 1.2 §19.8 [UnaryExpression]</see>.</remarks>
    UnaryPlus
}

/// <summary>
/// An arithmetic expression <c>left op right</c>. For the unary operators
/// (<see cref="ArithmeticOp.UnaryMinus"/>, <see cref="ArithmeticOp.UnaryPlus"/>)
/// <see cref="Right"/> is <c>null</c> and <see cref="Left"/> is the operand.
/// </summary>
/// <param name="Span">The source extent of the arithmetic expression.</param>
/// <param name="Left">The left operand (the sole operand for unary forms).</param>
/// <param name="Op">The arithmetic operator.</param>
/// <param name="Right">The right operand, or <c>null</c> for unary forms.</param>
/// <remarks>SPARQL <c>AdditiveExpression</c> / <c>MultiplicativeExpression</c> / <c>UnaryExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAdditiveExpression">SPARQL 1.2 §19.8 [AdditiveExpression]</see>.</remarks>
[DebuggerDisplay("({Op})")]
public sealed record ArithmeticExpression(SourceSpan Span, ExpressionNode Left, ArithmeticOp Op, ExpressionNode? Right) : ExpressionNode(Span);

/// <summary>The <c>value IN (set)</c> test.</summary>
/// <param name="Span">The source extent from the value through the candidate set.</param>
/// <param name="Value">The value tested for membership.</param>
/// <param name="Set">The candidate set.</param>
/// <remarks>SPARQL <c>RelationalExpression</c> (<c>IN</c>) / <c>ExpressionList</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
[DebuggerDisplay("(IN)")]
public sealed record InExpression(SourceSpan Span, ExpressionNode Value, IReadOnlyList<ExpressionNode> Set) : ExpressionNode(Span);

/// <summary>The <c>value NOT IN (set)</c> test.</summary>
/// <param name="Span">The source extent from the value through the candidate set.</param>
/// <param name="Value">The value tested for non-membership.</param>
/// <param name="Set">The candidate set.</param>
/// <remarks>SPARQL <c>RelationalExpression</c> (<c>NOT IN</c>) / <c>ExpressionList</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
[DebuggerDisplay("(NOT IN)")]
public sealed record NotInExpression(SourceSpan Span, ExpressionNode Value, IReadOnlyList<ExpressionNode> Set) : ExpressionNode(Span);

/// <summary>
/// A call to a named function: a user-defined or constructor function identified by IRI.
/// <see cref="IsDistinct"/> carries the optional leading <c>DISTINCT</c> of the call's argument
/// list, which the grammar reserves for custom aggregate calls; the translator lifts a
/// recognized aggregate call to <see cref="ExtensionAggregateExpression"/>, and a call that
/// stays scalar with the flag set evaluates to the expression error value.
/// </summary>
/// <param name="Span">The source extent from the function IRI through the closing parenthesis.</param>
/// <param name="Function">The function IRI.</param>
/// <param name="Arguments">The argument expressions.</param>
/// <param name="IsDistinct">Whether the argument list opened with <c>DISTINCT</c>.</param>
/// <remarks>SPARQL <c>iriOrFunction</c> / <c>ArgList</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rArgList">SPARQL 1.2 §19.8 [ArgList]</see>.</remarks>
[DebuggerDisplay("<{Function.Value}>(...)")]
public sealed record FunctionCallExpression(SourceSpan Span, IriRef Function, IReadOnlyList<ExpressionNode> Arguments, bool IsDistinct = false) : ExpressionNode(Span);

/// <summary>
/// A call to a reserved built-in function, identified by <see cref="BuiltInFunction"/>.
/// <see cref="IsDistinct"/> carries the <c>DISTINCT</c> flag where the grammar permits
/// it. <c>BOUND</c>, <c>IF</c>, and <c>COALESCE</c> have dedicated nodes and never
/// appear here.
/// </summary>
/// <param name="Span">The source extent from the function name through the closing parenthesis.</param>
/// <param name="Function">The built-in function.</param>
/// <param name="Arguments">The argument expressions.</param>
/// <param name="IsDistinct">Whether <c>DISTINCT</c> was given.</param>
/// <remarks>SPARQL <c>BuiltInCall</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBuiltInCall">SPARQL 1.2 §19.8 [BuiltInCall]</see>.</remarks>
[DebuggerDisplay("{Function}(...)")]
public sealed record BuiltInCallExpression(SourceSpan Span, BuiltInFunction Function, IReadOnlyList<ExpressionNode> Arguments, bool IsDistinct = false) : ExpressionNode(Span);

/// <summary>The <c>EXISTS { pattern }</c> test.</summary>
/// <param name="Span">The source extent from the <c>EXISTS</c> keyword through the inner pattern.</param>
/// <param name="Inner">The inner graph pattern.</param>
/// <remarks>SPARQL <c>ExistsFunc</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rExistsFunc">SPARQL 1.2 §19.8 [ExistsFunc]</see>.</remarks>
[DebuggerDisplay("EXISTS")]
public sealed record ExistsExpression(SourceSpan Span, GraphPattern Inner) : ExpressionNode(Span);

/// <summary>The <c>NOT EXISTS { pattern }</c> test.</summary>
/// <param name="Span">The source extent from the <c>NOT</c> keyword through the inner pattern.</param>
/// <param name="Inner">The inner graph pattern.</param>
/// <remarks>SPARQL <c>NotExistsFunc</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rNotExistsFunc">SPARQL 1.2 §19.8 [NotExistsFunc]</see>.</remarks>
[DebuggerDisplay("NOT EXISTS")]
public sealed record NotExistsExpression(SourceSpan Span, GraphPattern Inner) : ExpressionNode(Span);

/// <summary>The <c>IF(condition, ifTrue, ifFalse)</c> conditional.</summary>
/// <param name="Span">The source extent from the <c>IF</c> keyword through the closing parenthesis.</param>
/// <param name="Condition">The condition expression.</param>
/// <param name="IfTrue">The value when the condition's effective boolean value is true.</param>
/// <param name="IfFalse">The value otherwise.</param>
/// <remarks>SPARQL <c>BuiltInCall</c> (<c>IF</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rBuiltInCall">SPARQL 1.2 §19.8 [BuiltInCall]</see>.</remarks>
[DebuggerDisplay("IF")]
public sealed record IfExpression(SourceSpan Span, ExpressionNode Condition, ExpressionNode IfTrue, ExpressionNode IfFalse) : ExpressionNode(Span);

/// <summary>The <c>COALESCE(...)</c> first-non-error expression.</summary>
/// <param name="Span">The source extent from the <c>COALESCE</c> keyword through the closing parenthesis.</param>
/// <param name="Alternatives">The candidate expressions, in order.</param>
/// <remarks>SPARQL <c>BuiltInCall</c> (<c>COALESCE</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rBuiltInCall">SPARQL 1.2 §19.8 [BuiltInCall]</see>.</remarks>
[DebuggerDisplay("COALESCE")]
public sealed record CoalesceExpression(SourceSpan Span, IReadOnlyList<ExpressionNode> Alternatives) : ExpressionNode(Span);

/// <summary>The <c>BOUND(?var)</c> test.</summary>
/// <param name="Span">The source extent from the <c>BOUND</c> keyword through the closing parenthesis.</param>
/// <param name="Variable">The variable tested for being bound.</param>
/// <remarks>SPARQL <c>BuiltInCall</c> (<c>BOUND</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rBuiltInCall">SPARQL 1.2 §19.8 [BuiltInCall]</see>.</remarks>
[DebuggerDisplay("BOUND(?{Variable.Name})")]
public sealed record BoundExpression(SourceSpan Span, SparqlVariable Variable) : ExpressionNode(Span);

/// <summary>
/// An aggregate expression — a built-in keyword aggregate or an IRI-named extension
/// aggregate — appearing in a SELECT expression, <c>HAVING</c> condition, or ORDER BY
/// condition and lifted wholesale onto the group operator by the translator, which
/// replaces it with a reference to its per-group result variable.
/// </summary>
/// <param name="Span">The source extent from the aggregate name through the closing parenthesis.</param>
/// <param name="IsDistinct">Whether <c>DISTINCT</c> was given.</param>
/// <remarks>SPARQL <c>Aggregate</c> and the custom-aggregate <c>FunctionCall</c> form. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>.</remarks>
public abstract record AggregateExpression(SourceSpan Span, bool IsDistinct) : ExpressionNode(Span);

/// <summary>
/// A built-in keyword aggregate identified by <see cref="AggregateFunction"/>.
/// <see cref="IsCountStar"/> marks the <c>COUNT(*)</c> form, where
/// <see cref="Argument"/> is <c>null</c>.
/// </summary>
/// <param name="Span">The source extent from the aggregate name through the closing parenthesis.</param>
/// <param name="Function">The aggregate function.</param>
/// <param name="Argument">The aggregated expression, or <c>null</c> for <c>COUNT(*)</c>.</param>
/// <param name="IsDistinct">Whether <c>DISTINCT</c> was given.</param>
/// <param name="IsCountStar">Whether this is the <c>COUNT(*)</c> form.</param>
/// <param name="GroupConcatSeparator">The <c>GROUP_CONCAT</c> separator, or <c>null</c> for the default.</param>
/// <remarks>SPARQL <c>Aggregate</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>.</remarks>
[DebuggerDisplay("{Function}(agg)")]
public sealed record BuiltInAggregateExpression(
    SourceSpan Span,
    AggregateFunction Function,
    ExpressionNode? Argument,
    bool IsDistinct,
    bool IsCountStar,
    Utf8String? GroupConcatSeparator) : AggregateExpression(Span, IsDistinct);

/// <summary>
/// An IRI-named extension aggregate: a function call the translator recognized against the
/// engine's declared aggregate-function IRIs and lifted into aggregation. The node carries the
/// call's full parsed argument list so every argument stays visible to scope analysis and
/// structural comparison; the evaluation contract folds exactly one aggregated expression, so
/// any other arity answers the expression error value at evaluation.
/// </summary>
/// <param name="Span">The source extent from the function IRI through the closing parenthesis.</param>
/// <param name="FunctionIri">The aggregate function's IRI.</param>
/// <param name="Arguments">The parsed argument expressions, in call order.</param>
/// <param name="IsDistinct">Whether the argument list opened with <c>DISTINCT</c>.</param>
/// <remarks>SPARQL custom-aggregate <c>FunctionCall</c> via <c>ArgList</c>, whose optional <c>DISTINCT</c> the grammar reserves for custom aggregate calls. See <see href="https://www.w3.org/TR/sparql12-query/#rArgList">SPARQL 1.2 §19.8 [ArgList]</see>.</remarks>
[DebuggerDisplay("<{FunctionIri.Value}>(agg)")]
public sealed record ExtensionAggregateExpression(
    SourceSpan Span,
    IriRef FunctionIri,
    IReadOnlyList<ExpressionNode> Arguments,
    bool IsDistinct) : AggregateExpression(Span, IsDistinct);
