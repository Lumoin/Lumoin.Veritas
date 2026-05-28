using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;

namespace Lumoin.Veritas.Sparql.Translation;

/// <summary>
/// Translates a normalized SPARQL query's abstract syntax into the SPARQL algebra (a tree of
/// <see cref="AlgebraOperator"/>), the IR the executor and optimizer operate over. Implements the
/// translation algorithm of SPARQL 1.2 §18.2: group-graph-pattern translation with filter lifting,
/// <c>OPTIONAL</c> to <see cref="LeftJoin"/>, <c>UNION</c>/<c>GRAPH</c>/<c>SERVICE</c>/<c>MINUS</c> to
/// their operators, <c>BIND</c> to <see cref="Extend"/>, the empty group to <see cref="UnitTable"/>, and
/// the query-level solution modifiers (project expressions, <c>ORDER BY</c>, projection,
/// <c>DISTINCT</c>/<c>REDUCED</c>, and the <c>OFFSET</c>/<c>LIMIT</c> slice).
/// </summary>
/// <remarks>
/// <para>
/// The translator expects input already lowered by <see cref="SparqlNormalizer"/>: every
/// <see cref="BasicGraphPatternBlock"/> holds only plain <see cref="TriplePattern"/>s over the four core
/// term cases, and its <see cref="BasicGraphPatternBlock.StandaloneNodes"/> are empty. Translation is a
/// pure function of the AST and carries no state, so the type is static.
/// </para>
/// <para>
/// Three constructs are deferred to their own slices and are not produced here: aggregation
/// (<c>GROUP BY</c>/<c>HAVING</c> and aggregate expressions, which require the <c>Group</c>/
/// <c>AggregateJoin</c> operators) and sub-<c>SELECT</c> translation both raise
/// <see cref="NotSupportedException"/>; and property-path <em>lowering</em> to <see cref="Path"/> operators
/// is left for later, so a triple whose predicate is a <see cref="PropertyPathTerm"/> is carried inside its
/// <see cref="Bgp"/> verbatim (the property-path-translation slice extracts it per §18.2.2.4). An error node
/// in the recovered AST contributes nothing — a graph-pattern error becomes <see cref="UnitTable"/> and an
/// errored query-form head translates to the bare pattern algebra. SPARQL 1.2 §18.2 [Translation to the
/// SPARQL Algebra].
/// </para>
/// </remarks>
public static class SparqlTranslator
{
    /// <summary>
    /// Translates a normalized query into its algebra.
    /// </summary>
    /// <param name="query">The query to translate; expected to have been lowered by <see cref="SparqlNormalizer"/>.</param>
    /// <returns>The root of the query's algebra tree.</returns>
    /// <exception cref="NotSupportedException">A deferred construct (aggregation or a sub-<c>SELECT</c>) is present.</exception>
    public static AlgebraOperator Translate(SparqlQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        //The WHERE group pattern is translated first (§18.2.2); the form-specific head then wraps it.
        AlgebraOperator pattern = TranslatePattern(query.Where.Pattern);

        //A trailing VALUES block joins the pattern result (§18.2.5, final VALUES clause).
        if(query.Values is not null)
        {
            pattern = Join(pattern, new Table(query.Values));
        }

        //Grouping and aggregation are a separate slice; refuse them rather than emit a wrong tree.
        SolutionModifier modifier = query.Modifier;
        if(modifier.Group is not null || modifier.Having is not null)
        {
            throw new NotSupportedException("SPARQL aggregation (GROUP BY / HAVING) translation is a separate slice; this translator covers the non-aggregating core of SPARQL 1.2 §18.2.");
        }

        return query.Form switch
        {
            SelectQuery select => TranslateSelect(select, pattern, modifier),

            //ASK reports whether the pattern has any solution; the solution modifiers do not change that.
            AskQuery => pattern,

            //CONSTRUCT and DESCRIBE have no projection; ORDER BY and the slice still shape the solution
            //sequence that feeds template instantiation / resource description (a later, execution-time slice).
            ConstructQuery => ApplyOrderAndSlice(pattern, modifier),
            DescribeQuery => ApplyOrderAndSlice(pattern, modifier),

            //A recovered query whose form head is an error node still has a parsed WHERE pattern.
            ErrorQueryForm => pattern,

            _ => throw new InvalidOperationException($"Unexpected query-form kind {query.Form.GetType().Name} during SPARQL algebra translation.")
        };
    }

    /// <summary>Translates a graph pattern to its algebra (§18.2.2).</summary>
    /// <param name="pattern">The graph pattern to translate.</param>
    /// <returns>The algebra for the pattern.</returns>
    private static AlgebraOperator TranslatePattern(GraphPattern pattern)
    {
        return pattern switch
        {
            GroupGraphPattern group => TranslateGroup(group),
            BasicGraphPatternBlock block => TranslateBlock(block),
            UnionPattern union => new Union(TranslatePattern(union.Left), TranslatePattern(union.Right)),
            GraphGraphPattern graph => new Graph(graph.GraphTerm, TranslatePattern(graph.Inner)),
            ServicePattern service => new Service(service.Endpoint, TranslatePattern(service.Inner), service.IsSilent),
            ValuesPattern values => new Table(values.Data),

            //An error graph pattern carries its own diagnostics and contributes nothing to the algebra.
            ErrorGraphPattern => new UnitTable(),

            SubSelectPattern => throw new NotSupportedException("Sub-SELECT translation is a separate slice (SPARQL 1.2 §18.2.2 / §18.2.4)."),

            //OPTIONAL / MINUS / BIND / FILTER are only meaningful relative to their enclosing group and are
            //handled in TranslateGroup; reaching one here is a structural invariant violation.
            OptionalPattern or MinusPattern or BindPattern or FilterPattern => throw new InvalidOperationException($"{pattern.GetType().Name} is only valid as a group member and is handled during group translation, not as a standalone pattern."),

            _ => throw new InvalidOperationException($"Unexpected graph-pattern kind {pattern.GetType().Name} during SPARQL algebra translation.")
        };
    }

    /// <summary>
    /// Translates a group graph pattern (§18.2.2.6): the members are combined left to right, <c>FILTER</c>s
    /// are collected and lifted to constrain the whole group, and <c>OPTIONAL</c>/<c>MINUS</c>/<c>BIND</c>
    /// map to their operators while every other member is joined in.
    /// </summary>
    /// <param name="group">The group graph pattern to translate.</param>
    /// <returns>The algebra for the group; <see cref="UnitTable"/> for an empty group.</returns>
    private static AlgebraOperator TranslateGroup(GroupGraphPattern group)
    {
        List<ExpressionNode> filters = [];
        AlgebraOperator accumulated = new UnitTable();

        foreach(GraphPattern member in group.Members)
        {
            switch(member)
            {
                case FilterPattern filter:
                {
                    //Filters constrain the whole group regardless of where they appear; defer them to the end.
                    filters.Add(filter.Expression);

                    break;
                }

                case OptionalPattern optional:
                {
                    //A top-level FILTER inside the OPTIONAL becomes the left-join condition (§18.2.2.6).
                    AlgebraOperator right = TranslatePattern(optional.Inner);
                    accumulated = right is Filter lifted
                        ? new LeftJoin(accumulated, lifted.Input, lifted.Condition)
                        : new LeftJoin(accumulated, right, Condition: null);

                    break;
                }

                case MinusPattern minus:
                {
                    accumulated = new Minus(accumulated, TranslatePattern(minus.Inner));

                    break;
                }

                case BindPattern bind:
                {
                    accumulated = new Extend(accumulated, bind.AsVariable, bind.Expression);

                    break;
                }

                default:
                {
                    accumulated = Join(accumulated, TranslatePattern(member));

                    break;
                }
            }
        }

        if(filters.Count > 0)
        {
            accumulated = new Filter(Conjoin(filters), accumulated);
        }

        return accumulated;
    }

    /// <summary>Translates a basic graph pattern block to a <see cref="Bgp"/> (or <see cref="UnitTable"/> when empty).</summary>
    /// <param name="block">The normalized block; its triples are over core terms and its standalone nodes are empty.</param>
    /// <returns>A <see cref="Bgp"/> over the block's triples, or <see cref="UnitTable"/> (the join identity) for an empty run.</returns>
    private static AlgebraOperator TranslateBlock(BasicGraphPatternBlock block)
    {
        return block.Triples.Count == 0
            ? new UnitTable()
            : new Bgp(block.Triples);
    }

    /// <summary>
    /// Applies a <c>SELECT</c> query's head and modifiers over the pattern algebra (§18.2.5): project
    /// expressions become <see cref="Extend"/>s, then <c>ORDER BY</c>, then the projection (over the named
    /// columns, or every visible variable for <c>SELECT *</c>), then <c>DISTINCT</c>/<c>REDUCED</c>, then the
    /// <c>OFFSET</c>/<c>LIMIT</c> slice.
    /// </summary>
    /// <param name="select">The SELECT head.</param>
    /// <param name="pattern">The translated WHERE pattern (with any trailing VALUES already joined).</param>
    /// <param name="modifier">The solution modifiers.</param>
    /// <returns>The projected, modified algebra.</returns>
    private static AlgebraOperator TranslateSelect(SelectQuery select, AlgebraOperator pattern, SolutionModifier modifier)
    {
        AlgebraOperator current = pattern;

        //Project expressions ((expr AS ?v)) are bound before ORDER BY (so ordering may reference them) and
        //before projection (so it may keep them), in source order.
        foreach(SelectProjection projection in select.Projections)
        {
            if(projection is SelectExpressionAs expressionAs)
            {
                current = new Extend(current, expressionAs.AsVariable, expressionAs.Expression);
            }
        }

        if(modifier.Order is not null)
        {
            current = new OrderBy(current, modifier.Order.Conditions);
        }

        IReadOnlyList<SparqlVariable> projectionVariables = select.IsStar
            ? VisibleVariables(current)
            : ProjectionVariables(select.Projections);

        current = new Project(current, projectionVariables);

        if(select.IsDistinct)
        {
            current = new Distinct(current);
        }
        else if(select.IsReduced)
        {
            current = new Reduced(current);
        }

        return ApplySlice(current, modifier);
    }

    /// <summary>Applies the <c>ORDER BY</c> and <c>OFFSET</c>/<c>LIMIT</c> modifiers (the ones meaningful without a projection) over an input.</summary>
    /// <param name="input">The input algebra.</param>
    /// <param name="modifier">The solution modifiers.</param>
    /// <returns>The input wrapped in <see cref="OrderBy"/> and/or <see cref="Slice"/> as the modifiers require.</returns>
    private static AlgebraOperator ApplyOrderAndSlice(AlgebraOperator input, SolutionModifier modifier)
    {
        AlgebraOperator current = modifier.Order is not null
            ? new OrderBy(input, modifier.Order.Conditions)
            : input;

        return ApplySlice(current, modifier);
    }

    /// <summary>Wraps an input in a <see cref="Slice"/> when an <c>OFFSET</c> or <c>LIMIT</c> is present.</summary>
    /// <param name="input">The input algebra.</param>
    /// <param name="modifier">The solution modifiers.</param>
    /// <returns>The sliced input, or the input unchanged when neither bound is given.</returns>
    private static AlgebraOperator ApplySlice(AlgebraOperator input, SolutionModifier modifier)
    {
        return modifier.Offset is null && modifier.Limit is null
            ? input
            : new Slice(input, modifier.Offset ?? 0, modifier.Limit);
    }

    /// <summary>Collects a <c>SELECT</c> projection's column variables in source order: a bare variable, or the <c>AS</c> target of an expression column.</summary>
    /// <param name="projections">The projection columns.</param>
    /// <returns>The projected variables, in column order.</returns>
    private static List<SparqlVariable> ProjectionVariables(IReadOnlyList<SelectProjection> projections)
    {
        List<SparqlVariable> variables = new(projections.Count);
        foreach(SelectProjection projection in projections)
        {
            SparqlVariable variable = projection switch
            {
                SelectVariable bare => bare.Variable,
                SelectExpressionAs expressionAs => expressionAs.AsVariable,
                _ => throw new InvalidOperationException($"Unexpected projection kind {projection.GetType().Name} during SPARQL algebra translation.")
            };

            variables.Add(variable);
        }

        return variables;
    }

    /// <summary>Builds a single <see cref="Join"/>, simplifying away the join identity (§18.2.2.8: <c>Join(Z, A) = A</c>, <c>Join(A, Z) = A</c>).</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The right operand if the left is the identity, the left operand if the right is, otherwise their join.</returns>
    private static AlgebraOperator Join(AlgebraOperator left, AlgebraOperator right)
    {
        if(left is UnitTable)
        {
            return right;
        }

        if(right is UnitTable)
        {
            return left;
        }

        return new Join(left, right);
    }

    /// <summary>Conjoins collected filter expressions into one expression (§18.2.2.6), left-associatively.</summary>
    /// <param name="expressions">The filter expressions, in source order; always non-empty.</param>
    /// <returns>The single expression when there is one, otherwise the left-associated <see cref="AndExpression"/> chain.</returns>
    private static ExpressionNode Conjoin(List<ExpressionNode> expressions)
    {
        ExpressionNode conjunction = expressions[0];
        for(int i = 1; i < expressions.Count; i++)
        {
            ExpressionNode next = expressions[i];
            conjunction = new AndExpression(CombineSpans(conjunction.Span, next.Span), conjunction, next);
        }

        return conjunction;
    }

    /// <summary>Returns the variables visible (in scope) in an algebra subtree, in a deterministic source-appearance order with duplicates removed — the projection set for <c>SELECT *</c>.</summary>
    /// <param name="op">The algebra subtree.</param>
    /// <returns>The visible variables, in first-appearance order.</returns>
    private static List<SparqlVariable> VisibleVariables(AlgebraOperator op)
    {
        List<SparqlVariable> ordered = [];
        HashSet<SparqlVariable> seen = [];
        CollectVisible(op, ordered, seen);

        return ordered;
    }

    /// <summary>Walks an algebra subtree appending its in-scope variables to <paramref name="ordered"/>, respecting each operator's scope rule.</summary>
    /// <param name="op">The operator to scan.</param>
    /// <param name="ordered">The accumulating ordered list of distinct variables.</param>
    /// <param name="seen">The set tracking which variables are already recorded.</param>
    private static void CollectVisible(AlgebraOperator op, List<SparqlVariable> ordered, HashSet<SparqlVariable> seen)
    {
        switch(op)
        {
            case Bgp bgp:
            {
                foreach(TriplePattern triple in bgp.Patterns)
                {
                    CollectTermVariables(triple.Subject, ordered, seen);
                    CollectTermVariables(triple.Predicate, ordered, seen);
                    CollectTermVariables(triple.Object, ordered, seen);
                }

                break;
            }

            case Path path:
            {
                CollectTermVariables(path.Subject, ordered, seen);
                CollectTermVariables(path.Object, ordered, seen);

                break;
            }

            case Table table:
            {
                foreach(SparqlVariable variable in table.Data.Variables)
                {
                    Add(variable, ordered, seen);
                }

                break;
            }

            case Minus minus:
            {
                //Only the left operand's variables are in scope; the right is matched for compatibility only.
                CollectVisible(minus.Left, ordered, seen);

                break;
            }

            case Graph graph:
            {
                CollectVisible(graph.Input, ordered, seen);
                if(graph.Designator is GraphVariableTerm graphVariable)
                {
                    Add(graphVariable.Variable, ordered, seen);
                }

                break;
            }

            case Extend extend:
            {
                CollectVisible(extend.Input, ordered, seen);
                Add(extend.Variable, ordered, seen);

                break;
            }

            case Project project:
            {
                //A (sub-SELECT) projection caps the visible variables to its projected columns.
                foreach(SparqlVariable variable in project.Variables)
                {
                    Add(variable, ordered, seen);
                }

                break;
            }

            default:
            {
                //Transparent operators (Join, Union, LeftJoin, Filter, Service, Distinct, Reduced, OrderBy,
                //Slice, ToList, ToMultiSet) expose exactly their children's variables; UnitTable binds none.
                foreach(AlgebraOperator child in op.Children)
                {
                    CollectVisible(child, ordered, seen);
                }

                break;
            }
        }
    }

    /// <summary>Appends the variables a triple-pattern term contributes, recursing into a nested triple term.</summary>
    /// <param name="term">The term to scan.</param>
    /// <param name="ordered">The accumulating ordered list of distinct variables.</param>
    /// <param name="seen">The set tracking which variables are already recorded.</param>
    private static void CollectTermVariables(TriplePatternTerm term, List<SparqlVariable> ordered, HashSet<SparqlVariable> seen)
    {
        switch(term)
        {
            case VariableTerm variable:
            {
                Add(variable.Variable, ordered, seen);

                break;
            }

            case AstTripleTerm tripleTerm:
            {
                CollectTermVariables(tripleTerm.Inner.Subject, ordered, seen);
                CollectTermVariables(tripleTerm.Inner.Predicate, ordered, seen);
                CollectTermVariables(tripleTerm.Inner.Object, ordered, seen);

                break;
            }

            default:
            {
                //A ConstantTerm, a PropertyPathTerm, and an ErrorTriplePatternTerm bind no variables.
                break;
            }
        }
    }

    /// <summary>Records a variable in first-appearance order, skipping one already seen.</summary>
    /// <param name="variable">The variable to record.</param>
    /// <param name="ordered">The accumulating ordered list of distinct variables.</param>
    /// <param name="seen">The set tracking which variables are already recorded.</param>
    private static void Add(SparqlVariable variable, List<SparqlVariable> ordered, HashSet<SparqlVariable> seen)
    {
        if(seen.Add(variable))
        {
            ordered.Add(variable);
        }
    }

    /// <summary>Combines two spans into the covering span from the first's start to the second's end.</summary>
    /// <param name="start">The span at the start of the combined extent.</param>
    /// <param name="end">The span at the end of the combined extent.</param>
    /// <returns>The covering span.</returns>
    private static SourceSpan CombineSpans(SourceSpan start, SourceSpan end)
    {
        return new SourceSpan(start.StartByte, end.EndByte, start.StartLine, start.StartColumn, end.EndLine, end.EndColumn);
    }
}
