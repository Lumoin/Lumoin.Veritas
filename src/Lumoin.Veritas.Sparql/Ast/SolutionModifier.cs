using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The solution modifiers applied after the <c>WHERE</c> pattern: grouping,
/// having, ordering, and the offset/limit slice. Any component may be absent.
/// </summary>
/// <param name="Span">The source extent of the solution modifiers.</param>
/// <param name="Group">The <c>GROUP BY</c> clause, or <c>null</c>.</param>
/// <param name="Having">The <c>HAVING</c> clause, or <c>null</c>.</param>
/// <param name="Order">The <c>ORDER BY</c> clause, or <c>null</c>.</param>
/// <param name="Offset">The <c>OFFSET</c> value, or <c>null</c>.</param>
/// <param name="Limit">The <c>LIMIT</c> value, or <c>null</c>.</param>
/// <remarks>SPARQL <c>SolutionModifier</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSolutionModifier">SPARQL 1.2 §19.8 [SolutionModifier]</see>.</remarks>
[DebuggerDisplay("Modifier")]
public sealed record SolutionModifier(
    SourceSpan Span,
    GroupClause? Group,
    HavingClause? Having,
    OrderClause? Order,
    int? Offset,
    int? Limit);

/// <summary>A <c>GROUP BY</c> clause: the grouping conditions in order.</summary>
/// <param name="Span">The source extent of the clause.</param>
/// <param name="Conditions">The grouping conditions.</param>
/// <remarks>SPARQL <c>GroupClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupClause">SPARQL 1.2 §19.8 [GroupClause]</see>.</remarks>
[DebuggerDisplay("GROUP BY [{Conditions.Count}]")]
public sealed record GroupClause(SourceSpan Span, IReadOnlyList<GroupCondition> Conditions);

/// <summary>One <c>GROUP BY</c> condition: a variable, an expression, or an expression bound to a variable.</summary>
/// <param name="Span">The source extent of the condition.</param>
/// <remarks>SPARQL <c>GroupCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupCondition">SPARQL 1.2 §19.8 [GroupCondition]</see>.</remarks>
public abstract record GroupCondition(SourceSpan Span);

/// <summary>Grouping by a variable.</summary>
/// <param name="Span">The source extent of the condition.</param>
/// <param name="Variable">The grouping variable.</param>
/// <remarks>SPARQL <c>Var</c> in <c>GroupCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupCondition">SPARQL 1.2 §19.8 [GroupCondition]</see>.</remarks>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record GroupVariable(SourceSpan Span, SparqlVariable Variable) : GroupCondition(Span);

/// <summary>Grouping by an expression.</summary>
/// <param name="Span">The source extent of the condition.</param>
/// <param name="Expression">The grouping expression.</param>
/// <remarks>SPARQL <c>BuiltInCall</c> / <c>FunctionCall</c> in <c>GroupCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupCondition">SPARQL 1.2 §19.8 [GroupCondition]</see>.</remarks>
[DebuggerDisplay("expr")]
public sealed record GroupExpression(SourceSpan Span, ExpressionNode Expression) : GroupCondition(Span);

/// <summary>Grouping by an expression bound to a variable: <c>(expr AS ?var)</c>.</summary>
/// <param name="Span">The source extent of the condition.</param>
/// <param name="Expression">The grouping expression.</param>
/// <param name="AsVariable">The variable the expression binds to.</param>
/// <remarks>SPARQL <c>( Expression AS Var )</c> in <c>GroupCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupCondition">SPARQL 1.2 §19.8 [GroupCondition]</see>.</remarks>
[DebuggerDisplay("(expr AS ?{AsVariable.Name})")]
public sealed record GroupExpressionAs(SourceSpan Span, ExpressionNode Expression, SparqlVariable AsVariable) : GroupCondition(Span);

/// <summary>A <c>HAVING</c> clause: the conditions constraining grouped solutions.</summary>
/// <param name="Span">The source extent of the clause.</param>
/// <param name="Conditions">The having conditions.</param>
/// <remarks>SPARQL <c>HavingClause</c> / <c>HavingCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rHavingClause">SPARQL 1.2 §19.8 [HavingClause]</see>.</remarks>
[DebuggerDisplay("HAVING [{Conditions.Count}]")]
public sealed record HavingClause(SourceSpan Span, IReadOnlyList<ExpressionNode> Conditions);

/// <summary>An <c>ORDER BY</c> clause: the ordering conditions in priority order.</summary>
/// <param name="Span">The source extent of the clause.</param>
/// <param name="Conditions">The order conditions.</param>
/// <remarks>SPARQL <c>OrderClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOrderClause">SPARQL 1.2 §19.8 [OrderClause]</see>.</remarks>
[DebuggerDisplay("ORDER BY [{Conditions.Count}]")]
public sealed record OrderClause(SourceSpan Span, IReadOnlyList<OrderCondition> Conditions);

/// <summary>One <c>ORDER BY</c> condition with its direction.</summary>
/// <param name="Span">The source extent of the condition.</param>
/// <remarks>SPARQL <c>OrderCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOrderCondition">SPARQL 1.2 §19.8 [OrderCondition]</see>.</remarks>
public abstract record OrderCondition(SourceSpan Span);

/// <summary>An ascending order condition (the default direction).</summary>
/// <param name="Span">The source extent of the condition.</param>
/// <param name="Expression">The ordering key expression.</param>
/// <remarks>SPARQL <c>OrderCondition</c> (<c>ASC</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rOrderCondition">SPARQL 1.2 §19.8 [OrderCondition]</see>.</remarks>
[DebuggerDisplay("ASC")]
public sealed record OrderAscending(SourceSpan Span, ExpressionNode Expression) : OrderCondition(Span);

/// <summary>A descending order condition.</summary>
/// <param name="Span">The source extent of the condition.</param>
/// <param name="Expression">The ordering key expression.</param>
/// <remarks>SPARQL <c>OrderCondition</c> (<c>DESC</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rOrderCondition">SPARQL 1.2 §19.8 [OrderCondition]</see>.</remarks>
[DebuggerDisplay("DESC")]
public sealed record OrderDescending(SourceSpan Span, ExpressionNode Expression) : OrderCondition(Span);
