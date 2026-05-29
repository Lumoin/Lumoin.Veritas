using System.Collections.Generic;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Injects SHACL-SPARQL pre-bindings (<c>$this</c>, <c>$value</c>, <c>$PATH</c>, parameter variables) into a
/// validator/constraint query as an inline <c>VALUES</c> at the front of the <c>WHERE</c> group.
/// </summary>
/// <remarks>
/// The bindings must be an <em>inline</em> group member, not a trailing <c>VALUES</c> clause: a group's
/// <c>FILTER</c> is lifted to apply over the whole group, so a trailing <c>VALUES</c> (joined outside the group)
/// arrives too late for a filter-only validator such as <c>ASK { FILTER(?value = CONCAT($a, $b)) }</c>. Placing
/// the <c>VALUES</c> first inside the group makes the bindings visible to that lifted filter (and to any BGP),
/// matching the SHACL-SPARQL pre-binding semantics.
/// </remarks>
internal static class SparqlPreBinding
{
    /// <summary>Returns the query with <paramref name="values"/> injected as the first member of its <c>WHERE</c> group.</summary>
    /// <param name="query">The validator/constraint query.</param>
    /// <param name="values">The single-row pre-binding block.</param>
    /// <returns>The query whose <c>WHERE</c> group begins with the inline <c>VALUES</c>.</returns>
    public static SparqlQuery WithInlineValues(SparqlQuery query, ValuesClause values)
    {
        ValuesPattern valuesPattern = new(SourceSpan.None, values);
        GraphPattern pattern = query.Where.Pattern;

        GroupGraphPattern group = pattern is GroupGraphPattern existing
            ? new GroupGraphPattern(existing.Span, Prepend(valuesPattern, existing.Members))
            : new GroupGraphPattern(SourceSpan.None, [valuesPattern, pattern]);

        return query with { Where = query.Where with { Pattern = group } };
    }

    /// <summary>Builds a member list with <paramref name="head"/> first, then the existing members.</summary>
    /// <param name="head">The inline <c>VALUES</c> member to place first.</param>
    /// <param name="tail">The existing group members.</param>
    /// <returns>The combined member list.</returns>
    private static List<GraphPattern> Prepend(GraphPattern head, IReadOnlyList<GraphPattern> tail)
    {
        List<GraphPattern> members = new(tail.Count + 1) { head };
        members.AddRange(tail);

        return members;
    }
}
