using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Applies SHACL-SPARQL pre-binding (SHACL §5.2.1): the pre-bound variables (<c>$this</c>, <c>$value</c>,
/// <c>$PATH</c>, parameter variables) are <em>substituted</em> by their values throughout a validator/constraint
/// query — every occurrence in a triple pattern, a <c>FILTER</c>/<c>BIND</c> expression, a projection/<c>GROUP BY</c>/
/// <c>HAVING</c>/<c>ORDER BY</c> expression, and recursively inside <c>UNION</c> branches, nested groups, and
/// sub-<c>SELECT</c>s.
/// </summary>
/// <remarks>
/// Substitution — not an injected <c>VALUES</c> join — is the SHACL pre-binding semantics, and it is the only form
/// that reaches the scopes a join cannot: a sub-<c>SELECT</c> is a fresh variable scope, so a <c>$this</c> bound by an
/// outer <c>VALUES</c> is invisible inside it; likewise a <c>$this</c> referenced only within a <c>UNION</c> branch or
/// a nested <c>{…}</c> block is unbound there under a join. Replacing the variable by its value everywhere makes the
/// pre-binding visible in every scope. The pre-bound variable is not substituted in a binding-target position (a
/// <c>SELECT</c>/<c>GROUP BY</c> projection variable, a <c>BIND … AS</c>, or a <c>VALUES</c> variable) — SHACL §5.2.1
/// forbids a pre-bound variable from appearing there, so those positions are left as written.
/// </remarks>
internal static class SparqlPreBinding
{
    /// <summary>Returns the query with the pre-bindings carried by <paramref name="values"/> substituted throughout.</summary>
    /// <param name="query">The validator/constraint query.</param>
    /// <param name="values">The single-row pre-binding block (its variables paired with the row's values).</param>
    /// <returns>The query with every pre-bound variable replaced by its value; unchanged when there is nothing to bind.</returns>
    public static SparqlQuery Substitute(SparqlQuery query, ValuesClause values)
    {
        Dictionary<SparqlVariable, RdfTerm> bindings = BuildBindings(values);
        if(bindings.Count == 0)
        {
            return query;
        }

        return query with
        {
            Where = query.Where with { Pattern = SubstitutePattern(query.Where.Pattern, bindings) },
            Form = SubstituteForm(query.Form, bindings),
            Modifier = SubstituteModifier(query.Modifier, bindings),
        };
    }

    /// <summary>Builds the variable→value map from a single-row pre-binding block, skipping any <c>UNDEF</c> (null) cell.</summary>
    /// <param name="values">The pre-binding block.</param>
    /// <returns>The map of pre-bound variables to their values.</returns>
    private static Dictionary<SparqlVariable, RdfTerm> BuildBindings(ValuesClause values)
    {
        Dictionary<SparqlVariable, RdfTerm> bindings = [];
        if(values.Rows.Count == 0)
        {
            return bindings;
        }

        IReadOnlyList<RdfTerm?> row = values.Rows[0];
        for(int i = 0; i < values.Variables.Count && i < row.Count; i++)
        {
            if(row[i] is RdfTerm term)
            {
                bindings[values.Variables[i]] = term;
            }
        }

        return bindings;
    }

    /// <summary>Substitutes the pre-bindings throughout a graph-pattern tree, rebuilding it bottom-up over an explicit stack (no call-stack recursion).</summary>
    /// <param name="root">The root pattern.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted pattern.</returns>
    private static GraphPattern SubstitutePattern(GraphPattern root, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        Dictionary<GraphPattern, GraphPattern> rebuilt = new(ReferenceEqualityComparer.Instance);
        Stack<(GraphPattern Node, bool Combine)> work = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (GraphPattern node, bool combine) = work.Pop();
            if(!combine)
            {
                work.Push((node, Combine: true));
                foreach(GraphPattern child in PatternChildren(node))
                {
                    work.Push((child, Combine: false));
                }

                continue;
            }

            rebuilt[node] = RebuildPattern(node, rebuilt, bindings);
        }

        return rebuilt[root];
    }

    /// <summary>Returns the graph-pattern children of a pattern (the sub-patterns the substitution must descend into); a sub-<c>SELECT</c>'s child is its inner query's <c>WHERE</c> pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The child patterns, in order.</returns>
    private static IEnumerable<GraphPattern> PatternChildren(GraphPattern pattern)
    {
        return pattern switch
        {
            GroupGraphPattern group => group.Members,
            OptionalPattern optional => [optional.Inner],
            MinusPattern minus => [minus.Inner],
            UnionPattern union => [union.Left, union.Right],
            GraphGraphPattern graph => [graph.Inner],
            ServicePattern service => [service.Inner],
            SubSelectPattern subSelect => [subSelect.InnerQuery.Where.Pattern],
            _ => []
        };
    }

    /// <summary>Rebuilds a pattern from its substituted children and leaf-level substitutions (triple terms and expressions).</summary>
    /// <param name="pattern">The original pattern.</param>
    /// <param name="rebuilt">The map of already-rebuilt child patterns.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted pattern.</returns>
    private static GraphPattern RebuildPattern(GraphPattern pattern, Dictionary<GraphPattern, GraphPattern> rebuilt, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return pattern switch
        {
            GroupGraphPattern group => group with { Members = [.. group.Members.Select(member => rebuilt[member])] },
            OptionalPattern optional => optional with { Inner = rebuilt[optional.Inner] },
            MinusPattern minus => minus with { Inner = rebuilt[minus.Inner] },
            UnionPattern union => union with { Left = rebuilt[union.Left], Right = rebuilt[union.Right] },
            GraphGraphPattern graph => graph with { Inner = rebuilt[graph.Inner] },
            ServicePattern service => service with { Inner = rebuilt[service.Inner] },
            SubSelectPattern subSelect => subSelect with { InnerQuery = RebuildSubSelect(subSelect.InnerQuery, rebuilt, bindings) },
            BasicGraphPatternBlock block => block with
            {
                Triples = [.. block.Triples.Select(triple => SubstituteTriple(triple, bindings))],
                StandaloneNodes = [.. block.StandaloneNodes.Select(node => SubstituteTerm(node, bindings))],
            },
            FilterPattern filter => filter with { Expression = SubstituteExpression(filter.Expression, bindings) },
            BindPattern bind => bind with { Expression = SubstituteExpression(bind.Expression, bindings) },

            //A ValuesPattern's variables are binding targets a pre-bound variable may not occupy (SHACL §5.2.1); left as written.
            _ => pattern
        };
    }

    /// <summary>Rebuilds a sub-<c>SELECT</c>'s inner query with its already-substituted <c>WHERE</c> pattern and its projection/modifier expressions substituted.</summary>
    /// <param name="inner">The inner query.</param>
    /// <param name="rebuilt">The map of already-rebuilt child patterns.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted inner query.</returns>
    private static SparqlQuery RebuildSubSelect(SparqlQuery inner, Dictionary<GraphPattern, GraphPattern> rebuilt, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return inner with
        {
            Where = inner.Where with { Pattern = rebuilt[inner.Where.Pattern] },
            Form = SubstituteForm(inner.Form, bindings),
            Modifier = SubstituteModifier(inner.Modifier, bindings),
        };
    }

    /// <summary>Substitutes the pre-bindings in a query form's projection expressions; only a <c>SELECT</c>'s <c>(expr AS ?v)</c> projections carry expressions (a bare projection variable is a binding target and is left as written).</summary>
    /// <param name="form">The query form.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted query form.</returns>
    private static QueryForm SubstituteForm(QueryForm form, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return form switch
        {
            SelectQuery select => select with { Projections = [.. select.Projections.Select(projection => SubstituteProjection(projection, bindings))] },
            _ => form
        };
    }

    /// <summary>Substitutes the pre-bindings in a projection's expression; a bare projected variable is left as written.</summary>
    /// <param name="projection">The projection.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted projection.</returns>
    private static SelectProjection SubstituteProjection(SelectProjection projection, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return projection switch
        {
            SelectExpressionAs expressionAs => expressionAs with { Expression = SubstituteExpression(expressionAs.Expression, bindings) },
            _ => projection
        };
    }

    /// <summary>Substitutes the pre-bindings in a solution modifier's <c>GROUP BY</c> / <c>HAVING</c> / <c>ORDER BY</c> expressions.</summary>
    /// <param name="modifier">The solution modifier.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted modifier.</returns>
    private static SolutionModifier SubstituteModifier(SolutionModifier modifier, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        GroupClause? group = modifier.Group is { } existingGroup
            ? existingGroup with { Conditions = [.. existingGroup.Conditions.Select(condition => SubstituteGroupCondition(condition, bindings))] }
            : null;
        HavingClause? having = modifier.Having is { } existingHaving
            ? existingHaving with { Conditions = [.. existingHaving.Conditions.Select(expression => SubstituteExpression(expression, bindings))] }
            : null;
        OrderClause? order = modifier.Order is { } existingOrder
            ? existingOrder with { Conditions = [.. existingOrder.Conditions.Select(condition => SubstituteOrderCondition(condition, bindings))] }
            : null;

        return modifier with { Group = group, Having = having, Order = order };
    }

    /// <summary>Substitutes the pre-bindings in a <c>GROUP BY</c> condition's expression; a bare grouping variable is left as written.</summary>
    /// <param name="condition">The grouping condition.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted condition.</returns>
    private static GroupCondition SubstituteGroupCondition(GroupCondition condition, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return condition switch
        {
            GroupExpression expression => expression with { Expression = SubstituteExpression(expression.Expression, bindings) },
            GroupExpressionAs expressionAs => expressionAs with { Expression = SubstituteExpression(expressionAs.Expression, bindings) },
            _ => condition
        };
    }

    /// <summary>Substitutes the pre-bindings in an <c>ORDER BY</c> condition's key expression.</summary>
    /// <param name="condition">The order condition.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted condition.</returns>
    private static OrderCondition SubstituteOrderCondition(OrderCondition condition, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return condition switch
        {
            OrderAscending ascending => ascending with { Expression = SubstituteExpression(ascending.Expression, bindings) },
            OrderDescending descending => descending with { Expression = SubstituteExpression(descending.Expression, bindings) },
            _ => condition
        };
    }

    /// <summary>Substitutes the pre-bindings in a triple pattern's three positions.</summary>
    /// <param name="triple">The triple pattern.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted triple pattern.</returns>
    private static TriplePattern SubstituteTriple(TriplePattern triple, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return triple with
        {
            Subject = SubstituteTerm(triple.Subject, bindings),
            Predicate = SubstituteTerm(triple.Predicate, bindings),
            Object = SubstituteTerm(triple.Object, bindings),
        };
    }

    /// <summary>Substitutes the pre-bindings in a triple-pattern term: a pre-bound variable becomes a constant; a quoted triple term has its components substituted (over an explicit stack, no recursion); other terms are unchanged.</summary>
    /// <param name="term">The term.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted term.</returns>
    private static TriplePatternTerm SubstituteTerm(TriplePatternTerm term, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        if(term is VariableTerm { Variable: { } variable } variableTerm && bindings.TryGetValue(variable, out RdfTerm? value))
        {
            return new ConstantTerm(variableTerm.Span, value);
        }

        if(term is not AstTripleTerm)
        {
            return term;
        }

        //Quoted triple term: substitute its components bottom-up. Build each nested term once its components are ready.
        Dictionary<AstTripleTerm, AstTripleTerm> built = new(ReferenceEqualityComparer.Instance);
        Stack<(AstTripleTerm Term, bool Build)> work = new();
        work.Push(((AstTripleTerm)term, Build: false));

        while(work.Count > 0)
        {
            (AstTripleTerm current, bool build) = work.Pop();
            TriplePattern inner = current.Inner;

            if(!build)
            {
                work.Push((current, Build: true));
                if(inner.Subject is AstTripleTerm nestedSubject)
                {
                    work.Push((nestedSubject, Build: false));
                }

                if(inner.Object is AstTripleTerm nestedObject)
                {
                    work.Push((nestedObject, Build: false));
                }

                continue;
            }

            TriplePattern substitutedInner = inner with
            {
                Subject = SubstituteShallowTerm(inner.Subject, built, bindings),
                Predicate = SubstituteShallowTerm(inner.Predicate, built, bindings),
                Object = SubstituteShallowTerm(inner.Object, built, bindings),
            };
            built[current] = current with { Inner = substitutedInner };
        }

        return built[(AstTripleTerm)term];
    }

    /// <summary>Substitutes one already-shallow triple-term component: a pre-bound variable to a constant, a nested triple term to its already-built substitution, anything else unchanged.</summary>
    /// <param name="term">The component term.</param>
    /// <param name="built">The map of already-built nested triple terms.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted component.</returns>
    private static TriplePatternTerm SubstituteShallowTerm(TriplePatternTerm term, Dictionary<AstTripleTerm, AstTripleTerm> built, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return term switch
        {
            VariableTerm variableTerm when bindings.TryGetValue(variableTerm.Variable, out RdfTerm? value) => new ConstantTerm(variableTerm.Span, value),
            AstTripleTerm nested => built[nested],
            _ => term
        };
    }

    /// <summary>
    /// Substitutes the pre-bindings in an expression (iterative, via <see cref="ExpressionWalker.Transform"/>): a
    /// reference to a pre-bound variable becomes its constant value, and <c>BOUND</c> of a pre-bound variable becomes
    /// <c>true</c> — a pre-bound variable is always bound to a concrete value, so its boundness cannot be decided from
    /// the substituted constant (a constant is not a variable) and must be folded here.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted expression.</returns>
    private static ExpressionNode SubstituteExpression(ExpressionNode expression, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return ExpressionWalker.Transform(expression, node => SubstituteExpressionNode(node, bindings));
    }

    /// <summary>Substitutes one expression node: a pre-bound variable reference to its value, <c>BOUND(pre-bound)</c> to <c>true</c>, anything else unchanged.</summary>
    /// <param name="node">The expression node.</param>
    /// <param name="bindings">The variable→value map.</param>
    /// <returns>The substituted node.</returns>
    private static ExpressionNode SubstituteExpressionNode(ExpressionNode node, Dictionary<SparqlVariable, RdfTerm> bindings)
    {
        return node switch
        {
            VariableExpression variable when bindings.TryGetValue(variable.Variable, out RdfTerm? value) => new ConstantExpression(variable.Span, value),
            BoundExpression bound when bindings.ContainsKey(bound.Variable) => new ConstantExpression(bound.Span, BooleanTrue),
            _ => node
        };
    }

    /// <summary>The literal <c>"true"^^xsd:boolean</c>, folded in for <c>BOUND</c> of a pre-bound variable.</summary>
    private static Literal BooleanTrue { get; } = new(Utf8String.FromString("true"), new NamedNode(Vocabulary.Xsd.Boolean));
}
