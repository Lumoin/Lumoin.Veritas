using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Analysis;

/// <summary>
/// The SPARQL static "variable scope" semantic checks (§18.2.1 and the grammar notes) that a query must satisfy
/// beyond being grammatically well-formed: a query violating them is a <em>negative-syntax</em> test even though it
/// parses. The analyzer records an error diagnostic per violation rather than throwing, so the caller decides how to
/// surface them.
/// </summary>
/// <remarks>
/// <para>The checks, applied to the top query and every nested sub-<c>SELECT</c> independently:</para>
/// <list type="bullet">
///   <item>A <c>SELECT</c>/<c>BIND</c> assignment target (<c>(expr AS ?v)</c>) must not already be in scope — it
///   must not appear in the <c>WHERE</c> pattern, be a <c>GROUP BY</c> key, or repeat an earlier projection target.</item>
///   <item>A <c>BIND</c>'s target must not be in scope from the members preceding it in its group.</item>
///   <item>With grouping/aggregation, <c>SELECT *</c> is disallowed and every bare projected variable must be a
///   grouping key.</item>
///   <item>Aggregates must not be nested.</item>
/// </list>
/// <para>Variable scope follows §18.2.1: <c>MINUS</c> and <c>FILTER</c> contribute no in-scope variables, a
/// sub-<c>SELECT</c> contributes its projected variables (not its inner pattern's), and the walks use explicit
/// stacks (no recursion over the data-bearing pattern tree).</para>
/// </remarks>
public static class SparqlScopeAnalyzer
{
    private static Utf8String ScopeViolationCode { get; } = new("SP0010"u8.ToArray());

    /// <summary>Records a diagnostic for every static scope violation in a parsed request (queries only; updates have no such constraints here).</summary>
    /// <param name="request">The parsed request.</param>
    /// <param name="diagnostics">The bag violations are recorded in.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs under which an IRI function call is an aggregate (an engine's frozen <c>AggregateIris</c> profile); the empty set is the pure-SPARQL posture.</param>
    public static void Analyze(SparqlRequest request, DiagnosticBag diagnostics, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        System.ArgumentNullException.ThrowIfNull(diagnostics);
        System.ArgumentNullException.ThrowIfNull(aggregateFunctionIris);

        if(request is not SparqlQuery root)
        {
            return;
        }

        //Each query (the top one and every sub-SELECT, at any depth) is its own scope; a sub-SELECT pushed here is
        //analyzed in turn and its own nested sub-SELECTs discovered when it is processed.
        Stack<SparqlQuery> queries = new();
        queries.Push(root);

        while(queries.Count > 0)
        {
            SparqlQuery query = queries.Pop();
            AnalyzeQuery(query, diagnostics, aggregateFunctionIris);

            foreach(GraphPattern pattern in PatternsInScope(query.Where.Pattern))
            {
                if(pattern is SubSelectPattern subSelect)
                {
                    queries.Push(subSelect.InnerQuery);
                }
            }
        }
    }

    /// <summary>Applies the per-query scope checks (projection, BIND, grouping, nested aggregates).</summary>
    /// <param name="query">The query to check.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    private static void AnalyzeQuery(SparqlQuery query, DiagnosticBag diagnostics, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        CheckBindScope(query.Where.Pattern, diagnostics);
        CheckNestedAggregates(query, diagnostics, aggregateFunctionIris);

        if(query.Form is SelectQuery select)
        {
            CheckProjection(query, select, diagnostics, aggregateFunctionIris);
        }
    }

    /// <summary>Checks the <c>SELECT</c> projection: grouping rules, assignment targets not already in scope, and no duplicate targets.</summary>
    /// <param name="query">The enclosing query (for the <c>WHERE</c> pattern and modifiers).</param>
    /// <param name="select">The select head.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    private static void CheckProjection(SparqlQuery query, SelectQuery select, DiagnosticBag diagnostics, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        HashSet<SparqlVariable> whereVariables = InScopeVariables(query.Where.Pattern);
        HashSet<SparqlVariable> groupKeys = GroupKeys(query.Modifier.Group);
        bool grouping = query.Modifier.Group is not null || query.Modifier.Having is not null || HasAnyAggregate(query, select, aggregateFunctionIris);

        if(grouping && select.IsStar)
        {
            Report(diagnostics, select.Span, "SELECT * is not permitted with GROUP BY or aggregation; project the grouping keys and aggregates explicitly.");
        }

        HashSet<SparqlVariable> projected = [];
        foreach(SelectProjection projection in select.Projections)
        {
            if(projection is SelectVariable bare)
            {
                if(grouping && !groupKeys.Contains(bare.Variable))
                {
                    Report(diagnostics, bare.Span, $"Projected variable ?{bare.Variable.Name} is not a GROUP BY key; only grouping keys and aggregates may be projected from a grouped query.");
                }

                projected.Add(bare.Variable);
            }
            else if(projection is SelectExpressionAs assignment)
            {
                //After grouping, only the GROUP BY keys remain in scope for the projection — a non-key WHERE
                //variable is hidden, so it may be reused as an AS target (group-by-scope-1). Without grouping,
                //every WHERE-pattern variable is in scope.
                bool alreadyInScope = grouping
                    ? groupKeys.Contains(assignment.AsVariable)
                    : whereVariables.Contains(assignment.AsVariable);

                if(alreadyInScope || !projected.Add(assignment.AsVariable))
                {
                    Report(diagnostics, assignment.Span, $"The SELECT assignment target ?{assignment.AsVariable.Name} is already in scope (a GROUP BY key, a variable in the WHERE pattern, or an earlier projection); an AS target must be a fresh variable.");
                }

                if(grouping)
                {
                    CheckGroupedExpressionScope(assignment, groupKeys, diagnostics, aggregateFunctionIris);
                }
            }
        }
    }

    /// <summary>
    /// In a grouped/aggregate query, checks that every variable a projection expression references outside an
    /// aggregate is a <c>GROUP BY</c> key (§18.2.4.1). The walk treats built-in aggregates AND recognized
    /// extension-aggregate calls as leaves, so variables inside aggregate arguments are correctly excluded.
    /// </summary>
    /// <param name="assignment">The <c>(expr AS ?v)</c> projection.</param>
    /// <param name="groupKeys">The grouping-key variables.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    private static void CheckGroupedExpressionScope(SelectExpressionAs assignment, HashSet<SparqlVariable> groupKeys, DiagnosticBag diagnostics, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        foreach(ExpressionNode node in ExpressionWalker.TraverseOutsideAggregates(assignment.Expression, aggregateFunctionIris))
        {
            bool unguarded = node switch
            {
                VariableExpression variable => !groupKeys.Contains(variable.Variable),
                BoundExpression bound => !groupKeys.Contains(bound.Variable),
                _ => false
            };

            if(unguarded)
            {
                Report(diagnostics, assignment.Span, $"The SELECT assignment ?{assignment.AsVariable.Name} uses a variable that is neither a GROUP BY key nor inside an aggregate; only grouping keys and aggregates may appear in a projection from a grouped query.");

                return;
            }
        }
    }

    /// <summary>Checks that no <c>BIND</c> assigns a variable already in scope from the members preceding it in its group graph pattern.</summary>
    /// <param name="root">The query's <c>WHERE</c> pattern.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    private static void CheckBindScope(GraphPattern root, DiagnosticBag diagnostics)
    {
        foreach(GraphPattern pattern in PatternsInScope(root))
        {
            if(pattern is not GroupGraphPattern group)
            {
                continue;
            }

            HashSet<SparqlVariable> seen = [];
            foreach(GraphPattern member in group.Members)
            {
                if(member is BindPattern bind && seen.Contains(bind.AsVariable))
                {
                    Report(diagnostics, bind.Span, $"BIND assigns ?{bind.AsVariable.Name}, which is already in scope earlier in the group graph pattern; a BIND target must be a fresh variable.");
                }

                foreach(SparqlVariable variable in InScopeVariables(member))
                {
                    seen.Add(variable);
                }
            }
        }
    }

    /// <summary>Reports an error for any aggregate — built-in or recognized extension call — whose argument contains another aggregate (aggregates may not be nested).</summary>
    /// <param name="query">The query whose expressions are checked.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    private static void CheckNestedAggregates(SparqlQuery query, DiagnosticBag diagnostics, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        foreach(ExpressionNode expression in QueryExpressions(query))
        {
            foreach(ExpressionNode node in ExpressionWalker.Traverse(expression))
            {
                //The walk does not descend into a built-in aggregate's argument, so it is scanned from its
                //own node; a recognized extension call is an ordinary function call to the walker, so its
                //arguments are scanned directly.
                switch(node)
                {
                    case BuiltInAggregateExpression { Argument: { } argument } aggregate when ContainsAggregate(argument, aggregateFunctionIris):
                    {
                        Report(diagnostics, aggregate.Span, "An aggregate function may not be nested inside another aggregate function.");

                        break;
                    }

                    case FunctionCallExpression call when SparqlAggregateRecognition.IsRecognizedAggregateCall(call, aggregateFunctionIris, out _) && AnyArgumentContainsAggregate(call, aggregateFunctionIris):
                    {
                        Report(diagnostics, call.Span, "An aggregate function may not be nested inside another aggregate function.");

                        break;
                    }

                    default:
                    {
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Whether any argument of a call contains an aggregate.</summary>
    /// <param name="call">The function call.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    /// <returns><see langword="true"/> when an aggregate appears in any argument.</returns>
    private static bool AnyArgumentContainsAggregate(FunctionCallExpression call, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        foreach(ExpressionNode argument in call.Arguments)
        {
            if(ContainsAggregate(argument, aggregateFunctionIris))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether an expression tree contains an aggregate — a built-in aggregate node or a recognized extension-aggregate call.</summary>
    /// <param name="expression">The expression to inspect.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    /// <returns><see langword="true"/> when an aggregate appears anywhere in the tree.</returns>
    private static bool ContainsAggregate(ExpressionNode expression, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        foreach(ExpressionNode node in ExpressionWalker.Traverse(expression))
        {
            if(node is AggregateExpression || SparqlAggregateRecognition.IsRecognizedAggregateCall(node, aggregateFunctionIris, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The top-level expressions of a query: its projection / group-by / having / order expressions.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The expressions to scan for aggregates.</returns>
    private static IEnumerable<ExpressionNode> QueryExpressions(SparqlQuery query)
    {
        if(query.Form is SelectQuery select)
        {
            foreach(SelectProjection projection in select.Projections)
            {
                if(projection is SelectExpressionAs assignment)
                {
                    yield return assignment.Expression;
                }
            }
        }

        if(query.Modifier.Group is { } group)
        {
            foreach(GroupCondition condition in group.Conditions)
            {
                if(condition is GroupExpression expression)
                {
                    yield return expression.Expression;
                }
                else if(condition is GroupExpressionAs assignment)
                {
                    yield return assignment.Expression;
                }
            }
        }

        if(query.Modifier.Having is { } having)
        {
            foreach(ExpressionNode condition in having.Conditions)
            {
                yield return condition;
            }
        }

        if(query.Modifier.Order is { } order)
        {
            foreach(OrderCondition condition in order.Conditions)
            {
                yield return condition switch
                {
                    OrderAscending ascending => ascending.Expression,
                    OrderDescending descending => descending.Expression,
                    _ => throw new System.InvalidOperationException($"Unexpected order-condition kind {condition.GetType().Name}.")
                };
            }
        }
    }

    /// <summary>Whether a query is an aggregation query: it has <c>GROUP BY</c>/<c>HAVING</c> or any aggregate among its expressions.</summary>
    /// <param name="query">The query.</param>
    /// <param name="select">The select head.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    /// <returns><see langword="true"/> when grouping/aggregation applies.</returns>
    private static bool HasAnyAggregate(SparqlQuery query, SelectQuery select, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        _ = select;
        foreach(ExpressionNode expression in QueryExpressions(query))
        {
            if(ContainsAggregate(expression, aggregateFunctionIris))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The variables named by a <c>GROUP BY</c> clause (a grouping variable, or the target of a grouping expression).</summary>
    /// <param name="group">The group clause, or <see langword="null"/>.</param>
    /// <returns>The grouping-key variables.</returns>
    private static HashSet<SparqlVariable> GroupKeys(GroupClause? group)
    {
        HashSet<SparqlVariable> keys = [];
        if(group is null)
        {
            return keys;
        }

        foreach(GroupCondition condition in group.Conditions)
        {
            //`GROUP BY (?v)` (a parenthesised bare variable, no AS) keys on ?v exactly as `GROUP BY ?v`.
            SparqlVariable? key = condition switch
            {
                GroupVariable variable => variable.Variable,
                GroupExpressionAs assignment => assignment.AsVariable,
                GroupExpression { Expression: VariableExpression bare } => bare.Variable,
                _ => null
            };
            if(key is { } value)
            {
                keys.Add(value);
            }
        }

        return keys;
    }

    /// <summary>
    /// The variables in scope of a graph pattern, per SPARQL 1.2 §18.2.1: a triple block, a <c>BIND</c>
    /// target, the variables of a <c>VALUES</c> block, a variable <c>GRAPH</c>/<c>SERVICE</c> term, an
    /// <c>OPTIONAL</c>/<c>UNION</c> branch, and a sub-<c>SELECT</c>'s projected variables all contribute;
    /// <c>MINUS</c> and <c>FILTER</c> contribute none. The completion seam computes this over the parsed
    /// query prefix — everything before the caret — to surface the variables a caret may reference.
    /// </summary>
    /// <param name="root">The graph pattern.</param>
    /// <returns>The set of in-scope variables.</returns>
    public static HashSet<SparqlVariable> InScopeVariables(GraphPattern root)
    {
        HashSet<SparqlVariable> variables = [];
        Stack<GraphPattern> stack = new();
        stack.Push(root);

        while(stack.Count > 0)
        {
            GraphPattern pattern = stack.Pop();

            //FILTER references variables but binds none, and MINUS's variables are not in scope outside it, so
            //neither contributes nor is descended; a sub-SELECT contributes only its projected variables.
            if(pattern is BasicGraphPatternBlock block)
            {
                AddBlockVariables(block, variables);
            }
            else if(pattern is BindPattern bind)
            {
                variables.Add(bind.AsVariable);
            }
            else if(pattern is ValuesPattern values)
            {
                foreach(SparqlVariable variable in values.Data.Variables)
                {
                    variables.Add(variable);
                }
            }
            else if(pattern is SubSelectPattern subSelect)
            {
                AddProjectedVariables(subSelect.InnerQuery, variables);
            }
            else if(pattern is GroupGraphPattern group)
            {
                foreach(GraphPattern member in group.Members)
                {
                    stack.Push(member);
                }
            }
            else if(pattern is OptionalPattern optional)
            {
                stack.Push(optional.Inner);
            }
            else if(pattern is UnionPattern union)
            {
                stack.Push(union.Left);
                stack.Push(union.Right);
            }
            else if(pattern is GraphGraphPattern graph)
            {
                AddGraphTermVariable(graph.GraphTerm, variables);
                stack.Push(graph.Inner);
            }
            else if(pattern is ServicePattern service)
            {
                AddGraphTermVariable(service.Endpoint, variables);
                stack.Push(service.Inner);
            }
        }

        return variables;
    }

    /// <summary>Adds a sub-<c>SELECT</c>'s projected variables (the explicit targets, or its inner pattern's in-scope variables for <c>SELECT *</c>).</summary>
    /// <param name="query">The sub-select query.</param>
    /// <param name="variables">The set to add to.</param>
    private static void AddProjectedVariables(SparqlQuery query, HashSet<SparqlVariable> variables)
    {
        if(query.Form is not SelectQuery select)
        {
            return;
        }

        if(select.IsStar)
        {
            //SELECT * projects the inner pattern's in-scope variables; the call depth here is the query's
            //sub-SELECT nesting (author-bounded), not data depth.
            foreach(SparqlVariable variable in InScopeVariables(query.Where.Pattern))
            {
                variables.Add(variable);
            }

            foreach(SparqlVariable variable in GroupKeys(query.Modifier.Group))
            {
                variables.Add(variable);
            }

            return;
        }

        foreach(SelectProjection projection in select.Projections)
        {
            SparqlVariable? projected = projection switch
            {
                SelectVariable bare => bare.Variable,
                SelectExpressionAs assignment => assignment.AsVariable,
                _ => null
            };
            if(projected is { } variable)
            {
                variables.Add(variable);
            }
        }
    }

    /// <summary>Adds the variables of a graph term (the named-graph / service term) when it is a variable.</summary>
    /// <param name="term">The graph term.</param>
    /// <param name="variables">The set to add to.</param>
    private static void AddGraphTermVariable(GraphTerm term, HashSet<SparqlVariable> variables)
    {
        if(term is GraphVariableTerm variable)
        {
            variables.Add(variable.Variable);
        }
    }

    /// <summary>Adds every variable appearing in a basic graph pattern block's triples and standalone nodes.</summary>
    /// <param name="block">The basic graph pattern block.</param>
    /// <param name="variables">The set to add to.</param>
    private static void AddBlockVariables(BasicGraphPatternBlock block, HashSet<SparqlVariable> variables)
    {
        Stack<TriplePatternTerm> terms = new();
        foreach(TriplePattern triple in block.Triples)
        {
            terms.Push(triple.Subject);
            terms.Push(triple.Predicate);
            terms.Push(triple.Object);
        }

        foreach(TriplePatternTerm node in block.StandaloneNodes)
        {
            terms.Push(node);
        }

        while(terms.Count > 0)
        {
            TriplePatternTerm node = terms.Pop();

            //ConstantTerm and PropertyPathTerm bind no variables (a property path ranges over IRIs).
            if(node is VariableTerm variable)
            {
                variables.Add(variable.Variable);
            }
            else if(node is Ast.TripleTerm triple)
            {
                terms.Push(triple.Inner.Subject);
                terms.Push(triple.Inner.Predicate);
                terms.Push(triple.Inner.Object);
            }
            else if(node is ReifiedTriple reified)
            {
                terms.Push(reified.Inner.Subject);
                terms.Push(reified.Inner.Predicate);
                terms.Push(reified.Inner.Object);
                if(reified.Reifier is { } reifier)
                {
                    terms.Push(reifier);
                }
            }
            else if(node is CollectionTerm collection)
            {
                foreach(TriplePatternTerm item in collection.Items)
                {
                    terms.Push(item);
                }
            }
            else if(node is BlankNodePropertyListTerm blankNode)
            {
                PushPropertyList(terms, blankNode.Properties);
            }
            else if(node is AnnotatedObject annotated)
            {
                terms.Push(annotated.Object);
                foreach(Annotation annotation in annotated.Annotations)
                {
                    if(annotation is ReifierAnnotation { Reifier: { } reifier })
                    {
                        terms.Push(reifier);
                    }
                    else if(annotation is AnnotationBlock annotationBlock)
                    {
                        PushPropertyList(terms, annotationBlock.Properties);
                    }
                }
            }
        }
    }

    /// <summary>Pushes the verb and object terms of each entry in a property list onto the term work stack.</summary>
    /// <param name="terms">The term work stack.</param>
    /// <param name="properties">The property-list entries.</param>
    private static void PushPropertyList(Stack<TriplePatternTerm> terms, IReadOnlyList<PropertyListPath> properties)
    {
        foreach(PropertyListPath property in properties)
        {
            terms.Push(property.Verb);
            foreach(TriplePatternTerm @object in property.Objects)
            {
                terms.Push(@object);
            }
        }
    }

    /// <summary>Enumerates a pattern and its sub-patterns in this query's scope — stopping at a sub-<c>SELECT</c> (its inner pattern is a separate scope), which is itself yielded.</summary>
    /// <param name="root">The pattern to enumerate from.</param>
    /// <returns>The pattern nodes in this scope.</returns>
    private static IEnumerable<GraphPattern> PatternsInScope(GraphPattern root)
    {
        Stack<GraphPattern> stack = new();
        stack.Push(root);

        while(stack.Count > 0)
        {
            GraphPattern pattern = stack.Pop();
            yield return pattern;

            //A sub-SELECT is its own scope: it is yielded (so the caller can analyze it) but not descended into.
            IReadOnlyList<GraphPattern> children = pattern switch
            {
                GroupGraphPattern group => group.Members,
                OptionalPattern optional => [optional.Inner],
                MinusPattern minus => [minus.Inner],
                UnionPattern union => [union.Left, union.Right],
                GraphGraphPattern graph => [graph.Inner],
                ServicePattern service => [service.Inner],
                _ => []
            };
            foreach(GraphPattern child in children)
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>Records a scope-violation error diagnostic.</summary>
    /// <param name="diagnostics">The bag.</param>
    /// <param name="span">The offending span.</param>
    /// <param name="message">The message.</param>
    private static void Report(DiagnosticBag diagnostics, SourceSpan span, string message)
    {
        diagnostics.Add(new Diagnostic(ScopeViolationCode, DiagnosticSeverity.Error, span, Utf8Strings.From(message)));
    }
}
