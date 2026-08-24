using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The <c>WHERE</c> clause of a query: a single group graph pattern (the
/// surrounding braces produce a <see cref="GroupGraphPattern"/>).
/// </summary>
/// <param name="Span">The source extent of the WHERE clause.</param>
/// <param name="Pattern">The group graph pattern.</param>
/// <remarks>SPARQL <c>WhereClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rWhereClause">SPARQL 1.2 §19.8 [WhereClause]</see>.</remarks>
[DebuggerDisplay("WHERE")]
public sealed record WhereClause(SourceSpan Span, GraphPattern Pattern);
