using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
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
/// A triple whose predicate is a complex property path becomes a <see cref="Path"/> operator carrying the
/// structured <see cref="PropertyPathExpression"/> whole (§18.2.2.4); the <em>decomposition</em> of the
/// individual path forms — sequence/alternative/inverse into joins/unions/swaps, and the
/// <c>*</c>/<c>+</c>/<c>?</c>/negated-set evaluation semantics — is a later lowering slice. A sub-<c>SELECT</c>
/// becomes a <see cref="ToMultiSet"/> over its inner query's algebra, joined into the enclosing group.
/// Aggregation (an explicit <c>GROUP BY</c>/<c>HAVING</c>, or an aggregate expression triggering implicit
/// grouping) becomes <see cref="Group"/> then <see cref="AggregateJoin"/>, with each distinct aggregate bound
/// to a fresh result variable and the projection/<c>HAVING</c>/<c>ORDER BY</c> expressions rewritten to
/// reference those variables (§18.2.4.1). An error node in the recovered AST contributes nothing — a
/// graph-pattern error becomes <see cref="UnitTable"/> and an errored query-form head translates to the bare
/// pattern algebra. SPARQL 1.2 §18.2 [Translation to the SPARQL Algebra].
/// </para>
/// </remarks>
public static class SparqlTranslator
{
    /// <summary>
    /// Translates a normalized query into its algebra.
    /// </summary>
    /// <param name="query">The query to translate; expected to have been lowered by <see cref="SparqlNormalizer"/>.</param>
    /// <returns>The root of the query's algebra tree.</returns>
    public static AlgebraOperator Translate(SparqlQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        //The WHERE group pattern is translated first (§18.2.2); the form-specific head then wraps it.
        return ApplyQueryForm(query, TranslatePattern(query.Where.Pattern));
    }

    /// <summary>
    /// Applies a query's trailing <c>VALUES</c>, form-specific head, and solution modifiers over its
    /// already-translated WHERE pattern (§18.2.4 / §18.2.5). Used for the top-level query and, wrapped in
    /// <see cref="ToMultiSet"/>, for a sub-<c>SELECT</c>.
    /// </summary>
    /// <param name="query">The query whose head and modifiers are applied.</param>
    /// <param name="pattern">The query's already-translated WHERE pattern.</param>
    /// <returns>The query's algebra.</returns>
    private static AlgebraOperator ApplyQueryForm(SparqlQuery query, AlgebraOperator pattern)
    {
        //A trailing VALUES block joins the pattern result (§18.2.5, final VALUES clause).
        AlgebraOperator current = query.Values is not null
            ? Join(pattern, new Table(query.Values))
            : pattern;

        SolutionModifier modifier = query.Modifier;

        //The projection expressions feed both the aggregation trigger (an aggregate forces implicit grouping)
        //and, when no aggregation applies, the SELECT head; ORDER BY likewise carries through either path.
        IReadOnlyList<SelectProjection> projections = query.Form is SelectQuery selectForm ? selectForm.Projections : [];
        OrderClause? order = modifier.Order;

        //Aggregation applies on an explicit GROUP BY / HAVING, or on any aggregate appearing in the projection /
        //HAVING / ORDER BY expressions (implicit grouping). It rewrites the pattern (Group → AggregateJoin →
        //HAVING filter) and the projection / ORDER BY expressions (aggregates → their result variables) so the
        //SELECT / ORDER BY / slice steps below run over the rewritten forms (§18.2.4.1).
        List<AggregateBinding> aggregates = CollectAggregates(projections, modifier);
        if(modifier.Group is not null || modifier.Having is not null || aggregates.Count > 0)
        {
            AggregateRewrite rewrite = ApplyAggregation(current, projections, modifier, aggregates);
            current = rewrite.Pattern;
            projections = rewrite.Projections;
            order = rewrite.Order;
        }

        return query.Form switch
        {
            SelectQuery select => TranslateSelect(select, current, projections, order, modifier),

            //ASK reports whether the pattern has any solution; the solution modifiers do not change that.
            AskQuery => current,

            //CONSTRUCT and DESCRIBE have no projection; ORDER BY and the slice still shape the solution
            //sequence that feeds template instantiation / resource description (a later, execution-time slice).
            ConstructQuery => ApplyOrderAndSlice(current, order, modifier),
            DescribeQuery => ApplyOrderAndSlice(current, order, modifier),

            //A recovered query whose form head is an error node still has a parsed WHERE pattern.
            ErrorQueryForm => current,

            _ => throw new InvalidOperationException($"Unexpected query-form kind {query.Form.GetType().Name} during SPARQL algebra translation.")
        };
    }

    /// <summary>
    /// Applies the aggregation translation (§18.2.4.1) over the pattern: the input is grouped
    /// (<see cref="Group"/>) by the explicit <c>GROUP BY</c> conditions (or the single implicit group when there
    /// are none), the distinct aggregates are computed per group (<see cref="AggregateJoin"/>), <c>HAVING</c>
    /// becomes a <see cref="Filter"/> over the grouped solutions, and the projection / <c>ORDER BY</c>
    /// expressions are rewritten so each aggregate refers to its result variable.
    /// </summary>
    /// <param name="input">The pattern algebra to group and aggregate.</param>
    /// <param name="projections">The projection columns whose aggregates are rewritten.</param>
    /// <param name="modifier">The solution modifiers carrying the <c>GROUP BY</c> / <c>HAVING</c> / <c>ORDER BY</c> clauses.</param>
    /// <param name="aggregates">The distinct aggregates collected from the projection / <c>HAVING</c> / <c>ORDER BY</c>, each bound to its fresh result variable.</param>
    /// <returns>The grouped/aggregated pattern with the rewritten projection and ORDER BY.</returns>
    private static AggregateRewrite ApplyAggregation(AlgebraOperator input, IReadOnlyList<SelectProjection> projections, SolutionModifier modifier, List<AggregateBinding> aggregates)
    {
        //The aggregate-to-result-variable lookup the expression rewrites consult; keyed by value equality so the
        //two occurrences of one repeated aggregate map to the same variable.
        //Keyed by the source-position-insensitive aggregate so a lookup matches any textually identical
        //occurrence regardless of where it appears.
        Dictionary<AggregateExpression, SparqlVariable> bindings = new(aggregates.Count);
        foreach(AggregateBinding aggregate in aggregates)
        {
            bindings[NormalizeAggregate(aggregate.Aggregate)] = aggregate.Variable;
        }

        //Group then AggregateJoin: an empty GROUP BY is the single implicit group, and an empty aggregation list
        //(an explicit GROUP BY with no aggregate) still yields one solution per group bound to the grouping keys.
        AlgebraOperator current = new AggregateJoin(new Group(modifier.Group?.Conditions ?? [], input), aggregates);

        //HAVING constrains the grouped solutions and runs after the aggregate values are bound; its conditions are
        //conjoined into one filter over the AggregateJoin.
        if(modifier.Having is not null && modifier.Having.Conditions.Count > 0)
        {
            List<ExpressionNode> conditions = new(modifier.Having.Conditions.Count);
            foreach(ExpressionNode condition in modifier.Having.Conditions)
            {
                conditions.Add(RewriteAggregates(condition, bindings));
            }

            current = new Filter(Conjoin(conditions), current);
        }

        return new AggregateRewrite(current, RewriteProjections(projections, bindings), RewriteOrder(modifier.Order, bindings));
    }

    /// <summary>
    /// Collects the distinct aggregate expressions appearing across the projection, <c>HAVING</c>, and
    /// <c>ORDER BY</c> expressions (deduplicated independent of source position), in first-appearance order,
    /// assigning each a fresh internal result variable. The names use a leading <c>.</c> (<c>.agg0</c>,
    /// <c>.agg1</c>, …), which no user variable can carry, so they cannot collide with a query variable.
    /// </summary>
    /// <param name="projections">The projection columns to scan.</param>
    /// <param name="modifier">The solution modifiers carrying the <c>HAVING</c> / <c>ORDER BY</c> clauses.</param>
    /// <returns>The distinct aggregates, each bound to its fresh result variable, in first-appearance order; empty when there are none.</returns>
    private static List<AggregateBinding> CollectAggregates(IReadOnlyList<SelectProjection> projections, SolutionModifier modifier)
    {
        List<AggregateBinding> aggregates = [];
        Dictionary<AggregateExpression, SparqlVariable> seen = [];

        foreach(SelectProjection projection in projections)
        {
            if(projection is SelectExpressionAs expressionAs)
            {
                CollectAggregatesFrom(expressionAs.Expression, aggregates, seen);
            }
        }

        if(modifier.Having is not null)
        {
            foreach(ExpressionNode condition in modifier.Having.Conditions)
            {
                CollectAggregatesFrom(condition, aggregates, seen);
            }
        }

        if(modifier.Order is not null)
        {
            foreach(OrderCondition condition in modifier.Order.Conditions)
            {
                CollectAggregatesFrom(OrderExpression(condition), aggregates, seen);
            }
        }

        return aggregates;
    }

    /// <summary>Records the not-yet-seen aggregates in an expression tree, in first-appearance order, assigning each its fresh result variable.</summary>
    /// <param name="root">The expression to scan.</param>
    /// <param name="aggregates">The accumulating ordered list of distinct aggregate bindings.</param>
    /// <param name="seen">The map tracking which aggregates (by source-position-insensitive key) are already recorded, and to which variable.</param>
    private static void CollectAggregatesFrom(ExpressionNode root, List<AggregateBinding> aggregates, Dictionary<AggregateExpression, SparqlVariable> seen)
    {
        foreach(ExpressionNode node in ExpressionWalker.Traverse(root))
        {
            //Dedup ignores source position: two textually identical aggregates (e.g. COUNT(?o) in both the
            //projection and HAVING) compute one value, so they share one binding and one result variable.
            if(node is AggregateExpression aggregate && !seen.ContainsKey(NormalizeAggregate(aggregate)))
            {
                SparqlVariable variable = new(Utf8String.FromString($".agg{seen.Count}"));
                seen.Add(NormalizeAggregate(aggregate), variable);
                aggregates.Add(new AggregateBinding(variable, aggregate));
            }
        }
    }

    /// <summary>Rewrites the projection columns, replacing each aggregate in an expression column with a reference to its result variable; bare-variable columns are returned unchanged.</summary>
    /// <param name="projections">The projection columns.</param>
    /// <param name="bindings">The aggregate-to-result-variable lookup.</param>
    /// <returns>The rewritten projection columns, in order.</returns>
    private static List<SelectProjection> RewriteProjections(IReadOnlyList<SelectProjection> projections, Dictionary<AggregateExpression, SparqlVariable> bindings)
    {
        List<SelectProjection> rewritten = new(projections.Count);
        foreach(SelectProjection projection in projections)
        {
            rewritten.Add(projection is SelectExpressionAs expressionAs
                ? new SelectExpressionAs(expressionAs.Span, RewriteAggregates(expressionAs.Expression, bindings), expressionAs.AsVariable)
                : projection);
        }

        return rewritten;
    }

    /// <summary>Rewrites the <c>ORDER BY</c> conditions, replacing each aggregate in a key expression with a reference to its result variable.</summary>
    /// <param name="order">The <c>ORDER BY</c> clause, or <see langword="null"/>.</param>
    /// <param name="bindings">The aggregate-to-result-variable lookup.</param>
    /// <returns>The rewritten clause, or <see langword="null"/> when <paramref name="order"/> is <see langword="null"/>.</returns>
    private static OrderClause? RewriteOrder(OrderClause? order, Dictionary<AggregateExpression, SparqlVariable> bindings)
    {
        if(order is null)
        {
            return null;
        }

        List<OrderCondition> rewritten = new(order.Conditions.Count);
        foreach(OrderCondition condition in order.Conditions)
        {
            rewritten.Add(condition switch
            {
                OrderAscending ascending => new OrderAscending(ascending.Span, RewriteAggregates(ascending.Expression, bindings)),
                OrderDescending descending => new OrderDescending(descending.Span, RewriteAggregates(descending.Expression, bindings)),
                _ => throw new InvalidOperationException($"Unexpected order-condition kind {condition.GetType().Name} during SPARQL algebra translation.")
            });
        }

        return new OrderClause(order.Span, rewritten);
    }

    /// <summary>Replaces every aggregate in an expression with a reference to its result variable, leaving the rest of the tree intact.</summary>
    /// <param name="expression">The expression to rewrite.</param>
    /// <param name="bindings">The aggregate-to-result-variable lookup.</param>
    /// <returns>The rewritten expression, or the same instance when it holds no aggregate.</returns>
    private static ExpressionNode RewriteAggregates(ExpressionNode expression, Dictionary<AggregateExpression, SparqlVariable> bindings)
    {
        return ExpressionWalker.Transform(expression, node => node is AggregateExpression aggregate && bindings.TryGetValue(NormalizeAggregate(aggregate), out SparqlVariable variable)
            ? new VariableExpression(aggregate.Span, variable)
            : node);
    }

    /// <summary>
    /// Produces the source-position-insensitive form of an aggregate — its own span and every span inside its
    /// argument zeroed — used as the dedup / lookup key so two textually identical aggregates at different
    /// source positions compare equal under the records' value equality.
    /// </summary>
    /// <param name="aggregate">The aggregate to normalize.</param>
    /// <returns>The aggregate with all spans zeroed.</returns>
    private static AggregateExpression NormalizeAggregate(AggregateExpression aggregate)
    {
        //The argument is a general expression descended normally; the aggregate itself is a leaf of the walk,
        //so its own span is zeroed here rather than by the transform.
        ExpressionNode? argument = aggregate.Argument is null
            ? null
            : ExpressionWalker.Transform(aggregate.Argument, static node => node with { Span = default });

        return aggregate with { Span = default, Argument = argument };
    }

    /// <summary>Returns the key expression of an order condition, independent of its direction.</summary>
    /// <param name="condition">The order condition.</param>
    /// <returns>The condition's key expression.</returns>
    private static ExpressionNode OrderExpression(OrderCondition condition)
    {
        return condition switch
        {
            OrderAscending ascending => ascending.Expression,
            OrderDescending descending => descending.Expression,
            _ => throw new InvalidOperationException($"Unexpected order-condition kind {condition.GetType().Name} during SPARQL algebra translation.")
        };
    }

    /// <summary>
    /// Translates a graph pattern to its algebra (§18.2.2) over an explicit post-order stack (no recursion):
    /// every sub-pattern is translated before its parent and its result looked up by reference when the parent
    /// combines, so arbitrarily deep pattern nesting cannot overflow the stack.
    /// </summary>
    /// <param name="root">The graph pattern to translate.</param>
    /// <returns>The algebra for the pattern.</returns>
    private static AlgebraOperator TranslatePattern(GraphPattern root)
    {
        //Results are keyed by reference: two value-equal sibling patterns are distinct positions that must map
        //to distinct algebra. A parsed pattern is a tree, so each instance appears exactly once as a key.
        Dictionary<GraphPattern, AlgebraOperator> results = new(ReferenceEqualityComparer.Instance);
        Stack<(GraphPattern Node, bool Combine)> work = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (GraphPattern node, bool combine) = work.Pop();
            if(combine)
            {
                results[node] = TranslateNode(node, results);

                continue;
            }

            List<GraphPattern> subPatterns = SubPatterns(node);
            if(subPatterns.Count == 0)
            {
                //A leaf (block / VALUES / error / sub-SELECT), or a group whose only members are FILTER/BIND,
                //translates without awaiting any child result.
                results[node] = TranslateNode(node, results);
            }
            else
            {
                work.Push((node, Combine: true));
                for(int i = subPatterns.Count - 1; i >= 0; i--)
                {
                    work.Push((subPatterns[i], Combine: false));
                }
            }
        }

        return results[root];
    }

    /// <summary>
    /// Returns the sub-patterns of a graph pattern that translate to their own algebra and are looked up when
    /// the pattern combines — the operands the iterative <see cref="TranslatePattern"/> must process first.
    /// </summary>
    /// <param name="pattern">The graph pattern.</param>
    /// <returns>The sub-patterns to translate, in source order; empty for a leaf or a FILTER/BIND-only group.</returns>
    private static List<GraphPattern> SubPatterns(GraphPattern pattern)
    {
        switch(pattern)
        {
            case GroupGraphPattern group:
            {
                List<GraphPattern> subPatterns = [];
                foreach(GraphPattern member in group.Members)
                {
                    switch(member)
                    {
                        case OptionalPattern optional:
                        {
                            subPatterns.Add(optional.Inner);

                            break;
                        }

                        case MinusPattern minus:
                        {
                            subPatterns.Add(minus.Inner);

                            break;
                        }

                        case FilterPattern or BindPattern:
                        {
                            //A FILTER's / BIND's expression is not a sub-pattern; the group fold handles it inline.
                            break;
                        }

                        default:
                        {
                            subPatterns.Add(member);

                            break;
                        }
                    }
                }

                return subPatterns;
            }

            case UnionPattern union:
            {
                return [union.Left, union.Right];
            }

            case GraphGraphPattern graph:
            {
                return [graph.Inner];
            }

            case ServicePattern service:
            {
                return [service.Inner];
            }

            case SubSelectPattern subSelect:
            {
                //A sub-SELECT's inner WHERE pattern is translated first; its head/modifiers and the ToMultiSet
                //wrapper are applied when the SubSelectPattern combines. Routing it through the same post-order
                //keeps arbitrarily deep sub-SELECT nesting iterative.
                return [subSelect.InnerQuery.Where.Pattern];
            }

            default:
            {
                //Leaves: BasicGraphPatternBlock, ValuesPattern, ErrorGraphPattern.
                return [];
            }
        }
    }

    /// <summary>Builds the algebra for a single pattern node, given its already-translated sub-patterns (looked up by reference).</summary>
    /// <param name="node">The pattern node to translate.</param>
    /// <param name="results">The map of already-translated sub-patterns to their algebra.</param>
    /// <returns>The node's algebra.</returns>
    private static AlgebraOperator TranslateNode(GraphPattern node, Dictionary<GraphPattern, AlgebraOperator> results)
    {
        return node switch
        {
            GroupGraphPattern group => CombineGroup(group, results),
            BasicGraphPatternBlock block => TranslateBlock(block),
            UnionPattern union => new Union(results[union.Left], results[union.Right]),
            GraphGraphPattern graph => new Graph(graph.GraphTerm, results[graph.Inner]),
            ServicePattern service => new Service(service.Endpoint, results[service.Inner], service.IsSilent),
            ValuesPattern values => new Table(values.Data),

            //An error graph pattern carries its own diagnostics and contributes nothing to the algebra.
            ErrorGraphPattern => new UnitTable(),

            //A sub-SELECT becomes the multiset of its inner query's solutions joined into the enclosing group
            //(§18.2.4); the inner Project caps the visible variables, so only the inner projection is in scope.
            SubSelectPattern subSelect => new ToMultiSet(ApplyQueryForm(subSelect.InnerQuery, results[subSelect.InnerQuery.Where.Pattern])),

            //OPTIONAL / MINUS / BIND / FILTER are only meaningful relative to their enclosing group and are
            //handled in CombineGroup; reaching one here is a structural invariant violation.
            OptionalPattern or MinusPattern or BindPattern or FilterPattern => throw new InvalidOperationException($"{node.GetType().Name} is only valid as a group member and is handled during group translation, not as a standalone pattern."),

            _ => throw new InvalidOperationException($"Unexpected graph-pattern kind {node.GetType().Name} during SPARQL algebra translation.")
        };
    }

    /// <summary>
    /// Combines a group's members into algebra (§18.2.2.6): members combine left to right from the join
    /// identity, <c>FILTER</c>s are collected and lifted to constrain the whole group, and
    /// <c>OPTIONAL</c>/<c>MINUS</c>/<c>BIND</c> map to their operators. Each member's already-translated
    /// sub-pattern is read from <paramref name="results"/>.
    /// </summary>
    /// <param name="group">The group graph pattern.</param>
    /// <param name="results">The map of already-translated sub-patterns to their algebra.</param>
    /// <returns>The algebra for the group; <see cref="UnitTable"/> for an empty group.</returns>
    private static AlgebraOperator CombineGroup(GroupGraphPattern group, Dictionary<GraphPattern, AlgebraOperator> results)
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
                    AlgebraOperator right = results[optional.Inner];
                    accumulated = right is Filter lifted
                        ? new LeftJoin(accumulated, lifted.Input, lifted.Condition)
                        : new LeftJoin(accumulated, right, Condition: null);

                    break;
                }

                case MinusPattern minus:
                {
                    accumulated = new Minus(accumulated, results[minus.Inner]);

                    break;
                }

                case BindPattern bind:
                {
                    accumulated = new Extend(accumulated, bind.AsVariable, bind.Expression);

                    break;
                }

                default:
                {
                    accumulated = Join(accumulated, results[member]);

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

    /// <summary>
    /// Translates a basic graph pattern block (§18.2.2.4 / §18.2.2.5): triples with a plain predicate
    /// accumulate into <see cref="Bgp"/> runs, while each triple whose predicate is a property path becomes a
    /// <see cref="Path"/> operator, and the runs and paths join in source order.
    /// </summary>
    /// <param name="block">The normalized block; its triples are over core terms and its standalone nodes are empty.</param>
    /// <returns>The block's algebra: a single <see cref="Bgp"/> in the common all-plain case, a join of BGP runs and paths when paths are present, or <see cref="UnitTable"/> (the join identity) for an empty block.</returns>
    private static AlgebraOperator TranslateBlock(BasicGraphPatternBlock block)
    {
        AlgebraOperator result = new UnitTable();
        List<TriplePattern>? bgpRun = null;

        foreach(TriplePattern triple in block.Triples)
        {
            //The parser collapses a single-link path to a ConstantTerm predicate, so a PropertyPathTerm is
            //always a genuine complex path that earns its own Path operator (the path-form decomposition /
            //evaluation is a later lowering slice). A plain IRI or variable predicate stays a BGP triple.
            if(triple.Predicate is PropertyPathTerm pathTerm)
            {
                if(bgpRun is not null)
                {
                    result = Join(result, new Bgp(bgpRun));
                    bgpRun = null;
                }

                result = Join(result, new Path(triple.Subject, pathTerm.Path, triple.Object));
            }
            else
            {
                bgpRun ??= [];
                bgpRun.Add(triple);
            }
        }

        if(bgpRun is not null)
        {
            result = Join(result, new Bgp(bgpRun));
        }

        return result;
    }

    /// <summary>
    /// Applies a <c>SELECT</c> query's head and modifiers over the pattern algebra (§18.2.5): project
    /// expressions become <see cref="Extend"/>s, then <c>ORDER BY</c>, then the projection (over the named
    /// columns, or every visible variable for <c>SELECT *</c>), then <c>DISTINCT</c>/<c>REDUCED</c>, then the
    /// <c>OFFSET</c>/<c>LIMIT</c> slice.
    /// </summary>
    /// <param name="select">The SELECT head (its <c>DISTINCT</c>/<c>REDUCED</c>/<c>*</c> flags).</param>
    /// <param name="pattern">The translated WHERE pattern (with any trailing VALUES and aggregation already applied).</param>
    /// <param name="projections">The projection columns, with any aggregates already rewritten to their result variables.</param>
    /// <param name="order">The <c>ORDER BY</c> clause, with any aggregates already rewritten, or <see langword="null"/>.</param>
    /// <param name="modifier">The solution modifiers (the <c>OFFSET</c>/<c>LIMIT</c> slice).</param>
    /// <returns>The projected, modified algebra.</returns>
    private static AlgebraOperator TranslateSelect(SelectQuery select, AlgebraOperator pattern, IReadOnlyList<SelectProjection> projections, OrderClause? order, SolutionModifier modifier)
    {
        AlgebraOperator current = pattern;

        //Project expressions ((expr AS ?v)) are bound before ORDER BY (so ordering may reference them) and
        //before projection (so it may keep them), in source order.
        foreach(SelectProjection projection in projections)
        {
            if(projection is SelectExpressionAs expressionAs)
            {
                current = new Extend(current, expressionAs.AsVariable, expressionAs.Expression);
            }
        }

        if(order is not null)
        {
            current = new OrderBy(current, order.Conditions);
        }

        IReadOnlyList<SparqlVariable> projectionVariables = select.IsStar
            ? VisibleVariables(current)
            : ProjectionVariables(projections);

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
    /// <param name="order">The <c>ORDER BY</c> clause, with any aggregates already rewritten, or <see langword="null"/>.</param>
    /// <param name="modifier">The solution modifiers (the <c>OFFSET</c>/<c>LIMIT</c> slice).</param>
    /// <returns>The input wrapped in <see cref="OrderBy"/> and/or <see cref="Slice"/> as the modifiers require.</returns>
    private static AlgebraOperator ApplyOrderAndSlice(AlgebraOperator input, OrderClause? order, SolutionModifier modifier)
    {
        AlgebraOperator current = order is not null
            ? new OrderBy(input, order.Conditions)
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

    /// <summary>
    /// Returns the variables visible (in scope) in an algebra subtree, in deterministic source-appearance
    /// order with duplicates removed — the projection set for <c>SELECT *</c>. Walks an explicit work stack
    /// (no recursion), respecting each operator's scope rule.
    /// </summary>
    /// <param name="op">The algebra subtree.</param>
    /// <returns>The visible variables, in first-appearance order.</returns>
    private static List<SparqlVariable> VisibleVariables(AlgebraOperator op)
    {
        List<SparqlVariable> ordered = [];
        HashSet<SparqlVariable> seen = [];
        Stack<VisibleStep> work = new();
        work.Push(new VisibleStep(op, default));

        while(work.Count > 0)
        {
            VisibleStep step = work.Pop();
            if(step.Operator is null)
            {
                //A deferred variable, emitted after the subtree it follows (a GRAPH designator or BIND target).
                Add(step.Variable, ordered, seen);

                continue;
            }

            switch(step.Operator)
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
                    work.Push(new VisibleStep(minus.Left, default));

                    break;
                }

                case Graph graph:
                {
                    //The designator variable follows the inner pattern's variables, so push it first (it pops last).
                    if(graph.Designator is GraphVariableTerm graphVariable)
                    {
                        work.Push(new VisibleStep(null, graphVariable.Variable));
                    }

                    work.Push(new VisibleStep(graph.Input, default));

                    break;
                }

                case Extend extend:
                {
                    //The bound variable follows the input's variables, so push it first (it pops last).
                    work.Push(new VisibleStep(null, extend.Variable));
                    work.Push(new VisibleStep(extend.Input, default));

                    break;
                }

                case Project project:
                {
                    //A (sub-SELECT) projection caps the visible variables to its columns; push reversed so they pop in order.
                    for(int i = project.Variables.Count - 1; i >= 0; i--)
                    {
                        work.Push(new VisibleStep(null, project.Variables[i]));
                    }

                    break;
                }

                default:
                {
                    //Transparent operators (Join, Union, LeftJoin, Filter, Service, Distinct, Reduced, OrderBy,
                    //Slice, ToList, ToMultiSet) expose exactly their children's variables; UnitTable binds none.
                    //Push children reversed so they are processed in evaluation order.
                    IReadOnlyList<AlgebraOperator> children = step.Operator.Children;
                    for(int i = children.Count - 1; i >= 0; i--)
                    {
                        work.Push(new VisibleStep(children[i], default));
                    }

                    break;
                }
            }
        }

        return ordered;
    }

    /// <summary>Appends the variables a triple-pattern term contributes, descending into a nested triple term via an explicit stack (no recursion), preserving subject/predicate/object order.</summary>
    /// <param name="term">The term to scan.</param>
    /// <param name="ordered">The accumulating ordered list of distinct variables.</param>
    /// <param name="seen">The set tracking which variables are already recorded.</param>
    private static void CollectTermVariables(TriplePatternTerm term, List<SparqlVariable> ordered, HashSet<SparqlVariable> seen)
    {
        Stack<TriplePatternTerm> pending = new();
        pending.Push(term);

        while(pending.Count > 0)
        {
            TriplePatternTerm current = pending.Pop();
            switch(current)
            {
                case VariableTerm variable:
                {
                    Add(variable.Variable, ordered, seen);

                    break;
                }

                case AstTripleTerm tripleTerm:
                {
                    //Push the inner positions in reverse so they pop — and emit — in subject/predicate/object order.
                    pending.Push(tripleTerm.Inner.Object);
                    pending.Push(tripleTerm.Inner.Predicate);
                    pending.Push(tripleTerm.Inner.Subject);

                    break;
                }

                default:
                {
                    //A ConstantTerm, a PropertyPathTerm, and an ErrorTriplePatternTerm bind no variables.
                    break;
                }
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

    /// <summary>
    /// One step of the <see cref="VisibleVariables"/> work stack: either an operator to walk
    /// (<see cref="Operator"/> non-null) or a variable to emit (<see cref="Operator"/> null). The variable
    /// form is deferred so a <c>GRAPH</c> designator or <c>BIND</c> target is recorded after the subtree it
    /// follows, preserving source-appearance order.
    /// </summary>
    /// <param name="Operator">The operator to walk, or <see langword="null"/> when the step emits <see cref="Variable"/>.</param>
    /// <param name="Variable">The variable to emit when <see cref="Operator"/> is <see langword="null"/>.</param>
    private readonly record struct VisibleStep(AlgebraOperator? Operator, SparqlVariable Variable);

    /// <summary>
    /// The result of the aggregation translation: the grouped/aggregated pattern, and the projection and
    /// <c>ORDER BY</c> with their aggregates rewritten to reference the per-group result variables, so the
    /// SELECT-expression / ORDER BY / slice steps run over the rewritten forms.
    /// </summary>
    /// <param name="Pattern">The pattern after <see cref="Group"/> → <see cref="AggregateJoin"/> → optional <c>HAVING</c> <see cref="Filter"/>.</param>
    /// <param name="Projections">The projection columns with aggregates rewritten to their result variables.</param>
    /// <param name="Order">The <c>ORDER BY</c> clause with aggregates rewritten, or <see langword="null"/>.</param>
    private readonly record struct AggregateRewrite(AlgebraOperator Pattern, IReadOnlyList<SelectProjection> Projections, OrderClause? Order);
}
