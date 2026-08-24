using System;
using Lumoin.Veritas.Sparql.Algebra;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>Maps algebra operators to their trace-event operator kinds — the one total mapping the rewrite pipeline's and the interception registry's provenance events share.</summary>
internal static class SparqlExecutionOperators
{
    /// <summary>Maps an algebra operator to its trace operator kind; total over the closed operator set.</summary>
    /// <param name="node">The operator to map.</param>
    /// <returns>The trace operator kind.</returns>
    /// <exception cref="NotSupportedException">The operator is outside the closed set — an invariant violation.</exception>
    public static SparqlExecutionOperator Of(AlgebraOperator node)
    {
        return node switch
        {
            Bgp => SparqlExecutionOperator.Bgp,
            Path => SparqlExecutionOperator.Path,
            Join => SparqlExecutionOperator.Join,
            LeftJoin => SparqlExecutionOperator.LeftJoin,
            Union => SparqlExecutionOperator.Union,
            Minus => SparqlExecutionOperator.Minus,
            Filter => SparqlExecutionOperator.Filter,
            Extend => SparqlExecutionOperator.Extend,
            Project => SparqlExecutionOperator.Project,
            Distinct => SparqlExecutionOperator.Distinct,
            Reduced => SparqlExecutionOperator.Reduced,
            Slice => SparqlExecutionOperator.Slice,
            OrderBy => SparqlExecutionOperator.OrderBy,
            ToList => SparqlExecutionOperator.ToList,
            ToMultiSet => SparqlExecutionOperator.ToMultiSet,
            Group => SparqlExecutionOperator.Group,
            AggregateJoin => SparqlExecutionOperator.Aggregate,
            Graph => SparqlExecutionOperator.Graph,
            Service => SparqlExecutionOperator.Service,
            Table => SparqlExecutionOperator.Table,
            UnitTable => SparqlExecutionOperator.Unit,
            _ => throw new NotSupportedException($"Operator '{node.GetType().Name}' is outside the closed algebra set.")
        };
    }
}
