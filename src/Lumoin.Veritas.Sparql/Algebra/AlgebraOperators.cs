using System.Collections.Generic;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Algebra;

/// <summary>
/// Shared helpers for the algebra operators' <c>ComputeChildren</c> / <c>ComputeOutputVariables</c>
/// implementations: the leaf-operator empties, child-list builders, set union, and the collection of
/// variables appearing in triple-pattern terms.
/// </summary>
internal static class AlgebraScope
{
    /// <summary>The empty child list shared by leaf operators.</summary>
    public static IReadOnlyList<AlgebraOperator> NoChildren { get; } = [];

    /// <summary>The empty variable set shared by operators that bind nothing.</summary>
    public static IReadOnlySet<SparqlVariable> NoVariables { get; } = new HashSet<SparqlVariable>();

    /// <summary>Builds a single-element child list.</summary>
    /// <param name="child">The sole child operator.</param>
    /// <returns>A list holding <paramref name="child"/>.</returns>
    public static IReadOnlyList<AlgebraOperator> Children(AlgebraOperator child) => [child];

    /// <summary>Builds a two-element child list.</summary>
    /// <param name="left">The left child operator.</param>
    /// <param name="right">The right child operator.</param>
    /// <returns>A list holding <paramref name="left"/> then <paramref name="right"/>.</returns>
    public static IReadOnlyList<AlgebraOperator> Children(AlgebraOperator left, AlgebraOperator right) => [left, right];

    /// <summary>Returns the union of two variable sets.</summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns>A new set holding every variable in either operand.</returns>
    public static IReadOnlySet<SparqlVariable> Union(IReadOnlySet<SparqlVariable> left, IReadOnlySet<SparqlVariable> right)
    {
        HashSet<SparqlVariable> set = new(left);
        set.UnionWith(right);

        return set;
    }

    /// <summary>Returns a set extending an operator's output variables with one more variable.</summary>
    /// <param name="variables">The base variable set.</param>
    /// <param name="added">The variable to add.</param>
    /// <returns>A new set holding <paramref name="variables"/> plus <paramref name="added"/>.</returns>
    public static IReadOnlySet<SparqlVariable> With(IReadOnlySet<SparqlVariable> variables, SparqlVariable added)
    {
        HashSet<SparqlVariable> set = new(variables) { added };

        return set;
    }

    /// <summary>Collects the variables appearing across a set of triple patterns.</summary>
    /// <param name="patterns">The triple patterns.</param>
    /// <returns>The set of variables in any subject, predicate, or object position.</returns>
    public static IReadOnlySet<SparqlVariable> VariablesOf(IEnumerable<TriplePattern> patterns)
    {
        HashSet<SparqlVariable> set = [];
        foreach(TriplePattern pattern in patterns)
        {
            Collect(pattern.Subject, set);
            Collect(pattern.Predicate, set);
            Collect(pattern.Object, set);
        }

        return set;
    }

    /// <summary>Collects the variables appearing across the given terms.</summary>
    /// <param name="terms">The terms to scan.</param>
    /// <returns>The set of variables found.</returns>
    public static IReadOnlySet<SparqlVariable> VariablesOf(params TriplePatternTerm[] terms)
    {
        HashSet<SparqlVariable> set = [];
        foreach(TriplePatternTerm term in terms)
        {
            Collect(term, set);
        }

        return set;
    }

    /// <summary>Adds the variables a single term contributes, recursing into a nested triple term.</summary>
    /// <param name="term">The term to scan.</param>
    /// <param name="into">The set to add variables to.</param>
    private static void Collect(TriplePatternTerm term, HashSet<SparqlVariable> into)
    {
        switch(term)
        {
            case VariableTerm variable:
            {
                into.Add(variable.Variable);

                break;
            }

            case TripleTerm tripleTerm:
            {
                Collect(tripleTerm.Inner.Subject, into);
                Collect(tripleTerm.Inner.Predicate, into);
                Collect(tripleTerm.Inner.Object, into);

                break;
            }

            default:
            {
                //A ConstantTerm, a PropertyPathTerm (paths range over IRIs), and an ErrorTriplePatternTerm
                //bind no variables.
                break;
            }
        }
    }
}

/// <summary>A basic graph pattern: the conjunction of its triple patterns matched against the active graph.</summary>
/// <param name="Patterns">The triple patterns, already normalized to core terms.</param>
/// <remarks>SPARQL 1.2 §18.6 [BGP].</remarks>
public sealed record Bgp(IReadOnlyList<TriplePattern> Patterns) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.NoChildren;

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => AlgebraScope.VariablesOf(Patterns);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => this;
}

/// <summary>The join of two patterns: solutions compatible across both.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
/// <remarks>SPARQL 1.2 §18.6 [Join].</remarks>
public sealed record Join(AlgebraOperator Left, AlgebraOperator Right) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Left, Right);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => AlgebraScope.Union(Left.OutputVariables, Right.OutputVariables);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Join(children[0], children[1]);
}

/// <summary>An <c>OPTIONAL</c> join: left solutions extended by compatible right solutions, with an optional filter condition.</summary>
/// <param name="Left">The required (left) operand.</param>
/// <param name="Right">The optional (right) operand.</param>
/// <param name="Condition">The join condition (the <c>OPTIONAL</c>'s lifted filter), or <see langword="null"/> when there is none.</param>
/// <remarks>SPARQL 1.2 §18.6 [LeftJoin].</remarks>
public sealed record LeftJoin(AlgebraOperator Left, AlgebraOperator Right, ExpressionNode? Condition) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Left, Right);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => AlgebraScope.Union(Left.OutputVariables, Right.OutputVariables);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new LeftJoin(children[0], children[1], Condition);
}

/// <summary>The union of two patterns: all solutions of either operand.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
/// <remarks>SPARQL 1.2 §18.6 [Union].</remarks>
public sealed record Union(AlgebraOperator Left, AlgebraOperator Right) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Left, Right);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => AlgebraScope.Union(Left.OutputVariables, Right.OutputVariables);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Union(children[0], children[1]);
}

/// <summary>A <c>MINUS</c>: left solutions that have no compatible solution on the right. Only the left's variables are in scope.</summary>
/// <param name="Left">The left operand whose solutions are kept.</param>
/// <param name="Right">The right operand whose solutions remove compatible left solutions.</param>
/// <remarks>SPARQL 1.2 §18.6 [Minus].</remarks>
public sealed record Minus(AlgebraOperator Left, AlgebraOperator Right) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Left, Right);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Left.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Minus(children[0], children[1]);
}

/// <summary>A <c>FILTER</c>: the input solutions for which the condition evaluates to true.</summary>
/// <param name="Condition">The filter expression.</param>
/// <param name="Input">The filtered operand.</param>
/// <remarks>SPARQL 1.2 §18.6 [Filter].</remarks>
public sealed record Filter(ExpressionNode Condition, AlgebraOperator Input) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Filter(Condition, children[0]);
}

/// <summary>A <c>GRAPH</c> redirection: the input evaluated against the designated named graph. A variable designator is bound by the operator.</summary>
/// <param name="Designator">The graph designator (an IRI or a variable).</param>
/// <param name="Input">The pattern evaluated against the designated graph.</param>
/// <remarks>SPARQL 1.2 §18.6 [Graph].</remarks>
public sealed record Graph(GraphTerm Designator, AlgebraOperator Input) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables()
    {
        return Designator is GraphVariableTerm graphVariable
            ? AlgebraScope.With(Input.OutputVariables, graphVariable.Variable)
            : Input.OutputVariables;
    }

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Graph(Designator, children[0]);
}

/// <summary>An <c>Extend</c>: a <c>BIND</c> that adds a variable bound to an expression's value over each input solution.</summary>
/// <param name="Input">The extended operand.</param>
/// <param name="Variable">The variable the expression binds to.</param>
/// <param name="Expression">The bound expression.</param>
/// <remarks>SPARQL 1.2 §18.6 [Extend].</remarks>
public sealed record Extend(AlgebraOperator Input, SparqlVariable Variable, ExpressionNode Expression) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => AlgebraScope.With(Input.OutputVariables, Variable);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Extend(children[0], Variable, Expression);
}

/// <summary>A <c>SERVICE</c>: the input delegated to a federated endpoint. This build models it structurally; execution is a later milestone.</summary>
/// <param name="Endpoint">The endpoint designator (an IRI or a variable).</param>
/// <param name="Input">The pattern delegated to the service.</param>
/// <param name="Silent">Whether <c>SILENT</c> was given.</param>
/// <remarks>SPARQL 1.2 §18.6 [Service].</remarks>
public sealed record Service(GraphTerm Endpoint, AlgebraOperator Input, bool Silent) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Service(Endpoint, children[0], Silent);
}

/// <summary>Inline data: a multiset of solutions over fixed variables, from a <c>VALUES</c> block.</summary>
/// <param name="Data">The inline-data block, holding the variables and rows.</param>
/// <remarks>SPARQL 1.2 §18.6 [Table] / [ToMultiSet] of data.</remarks>
public sealed record Table(ValuesClause Data) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.NoChildren;

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => new HashSet<SparqlVariable>(Data.Variables);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => this;
}

/// <summary>The unit table: a single empty solution — the identity of join, the algebra of an empty group <c>{}</c>.</summary>
/// <remarks>SPARQL 1.2 §18.6 (the table Z with one empty solution mapping).</remarks>
public sealed record UnitTable : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.NoChildren;

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => AlgebraScope.NoVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => this;
}

/// <summary>A property-path match between two endpoints.</summary>
/// <param name="Subject">The subject endpoint.</param>
/// <param name="PathExpression">The property-path expression connecting the endpoints.</param>
/// <param name="Object">The object endpoint.</param>
/// <remarks>SPARQL 1.2 §18.6 [Path] / path operators (ZeroOrMorePath, OneOrMorePath, ZeroOrOnePath, NegatedPropertySet).</remarks>
public sealed record Path(TriplePatternTerm Subject, PropertyPathExpression PathExpression, TriplePatternTerm Object) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.NoChildren;

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => AlgebraScope.VariablesOf(Subject, Object);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => this;
}

/// <summary>A projection: restrict each solution to the named variables.</summary>
/// <param name="Input">The projected operand.</param>
/// <param name="Variables">The variables retained in the result.</param>
/// <remarks>SPARQL 1.2 §18.6 [Project].</remarks>
public sealed record Project(AlgebraOperator Input, IReadOnlyList<SparqlVariable> Variables) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => new HashSet<SparqlVariable>(Variables);

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Project(children[0], Variables);
}

/// <summary>A <c>DISTINCT</c>: eliminate duplicate solutions.</summary>
/// <param name="Input">The operand whose duplicates are removed.</param>
/// <remarks>SPARQL 1.2 §18.6 [Distinct].</remarks>
public sealed record Distinct(AlgebraOperator Input) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Distinct(children[0]);
}

/// <summary>A <c>REDUCED</c>: permit, but do not require, duplicate elimination.</summary>
/// <param name="Input">The operand.</param>
/// <remarks>SPARQL 1.2 §18.6 [Reduced].</remarks>
public sealed record Reduced(AlgebraOperator Input) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Reduced(children[0]);
}

/// <summary>An <c>ORDER BY</c>: order the input solutions by the conditions, in priority order.</summary>
/// <param name="Input">The ordered operand.</param>
/// <param name="Conditions">The ordering conditions (the AST <see cref="OrderCondition"/> forms), in priority order.</param>
/// <remarks>SPARQL 1.2 §18.6 [OrderBy].</remarks>
public sealed record OrderBy(AlgebraOperator Input, IReadOnlyList<OrderCondition> Conditions) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new OrderBy(children[0], Conditions);
}

/// <summary>A slice: the <c>OFFSET</c>/<c>LIMIT</c> window over the input solutions.</summary>
/// <param name="Input">The sliced operand.</param>
/// <param name="Offset">The number of solutions to skip (0 when no <c>OFFSET</c>).</param>
/// <param name="Limit">The maximum number of solutions to return, or <see langword="null"/> for no limit.</param>
/// <remarks>SPARQL 1.2 §18.6 [Slice].</remarks>
public sealed record Slice(AlgebraOperator Input, int Offset, int? Limit) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new Slice(children[0], Offset, Limit);
}

/// <summary>A sequence: materialize the input as an ordered list (the operand of <c>OrderBy</c> in the algebra).</summary>
/// <param name="Input">The operand to materialize.</param>
/// <remarks>SPARQL 1.2 §18.6 [ToList].</remarks>
public sealed record ToList(AlgebraOperator Input) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new ToList(children[0]);
}

/// <summary>A conversion to a multiset of solutions (e.g. the result of a sub-select joined into an enclosing pattern).</summary>
/// <param name="Input">The operand to convert.</param>
/// <remarks>SPARQL 1.2 §18.6 [ToMultiSet].</remarks>
public sealed record ToMultiSet(AlgebraOperator Input) : AlgebraOperator
{
    /// <inheritdoc/>
    protected override IReadOnlyList<AlgebraOperator> ComputeChildren() => AlgebraScope.Children(Input);

    /// <inheritdoc/>
    protected override IReadOnlySet<SparqlVariable> ComputeOutputVariables() => Input.OutputVariables;

    /// <inheritdoc/>
    internal override AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children) => new ToMultiSet(children[0]);
}
