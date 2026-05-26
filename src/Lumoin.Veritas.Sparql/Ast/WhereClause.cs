using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The <c>WHERE</c> clause of a query: a single group graph pattern (the
/// surrounding braces produce a <see cref="GroupGraphPattern"/>).
/// </summary>
/// <param name="Pattern">The group graph pattern.</param>
[DebuggerDisplay("WHERE")]
public sealed record WhereClause(GraphPattern Pattern);
