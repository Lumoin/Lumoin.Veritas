using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The solution modifiers applied after the <c>WHERE</c> pattern: grouping,
/// having, ordering, and the offset/limit slice. Any component may be absent.
/// </summary>
/// <param name="Group">The <c>GROUP BY</c> clause, or <c>null</c>.</param>
/// <param name="Having">The <c>HAVING</c> clause, or <c>null</c>.</param>
/// <param name="Order">The <c>ORDER BY</c> clause, or <c>null</c>.</param>
/// <param name="Offset">The <c>OFFSET</c> value, or <c>null</c>.</param>
/// <param name="Limit">The <c>LIMIT</c> value, or <c>null</c>.</param>
[DebuggerDisplay("Modifier")]
public sealed record SolutionModifier(
    GroupClause? Group,
    HavingClause? Having,
    OrderClause? Order,
    int? Offset,
    int? Limit);

/// <summary>A <c>GROUP BY</c> clause: the grouping conditions in order.</summary>
/// <param name="Conditions">The grouping conditions.</param>
[DebuggerDisplay("GROUP BY [{Conditions.Count}]")]
public sealed record GroupClause(IReadOnlyList<GroupCondition> Conditions);

/// <summary>One <c>GROUP BY</c> condition: a variable, an expression, or an expression bound to a variable.</summary>
public abstract record GroupCondition;

/// <summary>Grouping by a variable.</summary>
/// <param name="Variable">The grouping variable.</param>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record GroupVariable(SparqlVariable Variable) : GroupCondition;

/// <summary>Grouping by an expression.</summary>
/// <param name="Expression">The grouping expression.</param>
[DebuggerDisplay("expr")]
public sealed record GroupExpression(ExpressionNode Expression) : GroupCondition;

/// <summary>Grouping by an expression bound to a variable: <c>(expr AS ?var)</c>.</summary>
/// <param name="Expression">The grouping expression.</param>
/// <param name="AsVariable">The variable the expression binds to.</param>
[DebuggerDisplay("(expr AS ?{AsVariable.Name})")]
public sealed record GroupExpressionAs(ExpressionNode Expression, SparqlVariable AsVariable) : GroupCondition;

/// <summary>A <c>HAVING</c> clause: the conditions constraining grouped solutions.</summary>
/// <param name="Conditions">The having conditions.</param>
[DebuggerDisplay("HAVING [{Conditions.Count}]")]
public sealed record HavingClause(IReadOnlyList<ExpressionNode> Conditions);

/// <summary>An <c>ORDER BY</c> clause: the ordering conditions in priority order.</summary>
/// <param name="Conditions">The order conditions.</param>
[DebuggerDisplay("ORDER BY [{Conditions.Count}]")]
public sealed record OrderClause(IReadOnlyList<OrderCondition> Conditions);

/// <summary>One <c>ORDER BY</c> condition with its direction.</summary>
public abstract record OrderCondition;

/// <summary>An ascending order condition (the default direction).</summary>
/// <param name="Expression">The ordering key expression.</param>
[DebuggerDisplay("ASC")]
public sealed record OrderAscending(ExpressionNode Expression) : OrderCondition;

/// <summary>A descending order condition.</summary>
/// <param name="Expression">The ordering key expression.</param>
[DebuggerDisplay("DESC")]
public sealed record OrderDescending(ExpressionNode Expression) : OrderCondition;
