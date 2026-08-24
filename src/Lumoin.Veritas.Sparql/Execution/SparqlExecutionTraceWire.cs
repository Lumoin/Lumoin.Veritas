using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Projects a <see cref="SparqlExecutionTraceEvent"/> to the transport-neutral <see cref="TraceWireEvent"/>
/// shape the first-party hosts stream to a consuming surface. The kind tokens are fixed:
/// <c>operator</c> (an operator evaluation, with its strategy and row shape in the detail),
/// <c>rewrite-applied</c> / <c>rewrite-abstained</c> (a rewrite rule's verdict at one position), and
/// <c>interception</c> (a fast-path entry answering or annotating). Projection allocates, so callers on
/// untraced or subscriber-free paths skip it entirely.
/// </summary>
public static class SparqlExecutionTraceWire
{
    /// <summary>The wire kind of an operator-evaluated event.</summary>
    public const string OperatorKind = "operator";

    /// <summary>The wire kind of a rewrite rule that applied at a position.</summary>
    public const string RewriteAppliedKind = "rewrite-applied";

    /// <summary>The wire kind of a rewrite rule that matched but abstained at a position.</summary>
    public const string RewriteAbstainedKind = "rewrite-abstained";

    /// <summary>The wire kind of an evaluation interception that answered or annotated a position.</summary>
    public const string InterceptionKind = "interception";

    /// <summary>Projects one execution trace event to its wire shape.</summary>
    /// <param name="evt">The event to project.</param>
    /// <returns>The wire event.</returns>
    public static TraceWireEvent ToWire(in SparqlExecutionTraceEvent evt)
    {
        return evt.Kind switch
        {
            SparqlExecutionEventKind.RewriteApplied => new TraceWireEvent(
                evt.CorrelationId,
                evt.SequenceNumber,
                evt.RewriteApplication == AlgebraRewriteApplication.Applied ? RewriteAppliedKind : RewriteAbstainedKind,
                evt.Label,
                $"pass {evt.RewritePass} at {OperatorName(evt.Operator)}"),
            SparqlExecutionEventKind.InterceptionApplied => new TraceWireEvent(
                evt.CorrelationId,
                evt.SequenceNumber,
                InterceptionKind,
                evt.Label,
                evt.RowsOut >= 0 ? $"answered {evt.RowsOut} rows at {OperatorName(evt.Operator)}" : $"annotated at {OperatorName(evt.Operator)}"),
            _ => new TraceWireEvent(
                evt.CorrelationId,
                evt.SequenceNumber,
                OperatorKind,
                OperatorName(evt.Operator),
                OperatorDetail(in evt))
        };
    }

    /// <summary>Formats an operator evaluation's detail: the strategy with the input and output row shape; a leaf carries no input counts.</summary>
    /// <param name="evt">The operator-evaluated event.</param>
    /// <returns>The detail text.</returns>
    private static string OperatorDetail(in SparqlExecutionTraceEvent evt)
    {
        string strategy = StrategyName(evt.Strategy);

        if(evt.RowsLeft < 0)
        {
            return $"{strategy}: {evt.RowsOut} rows";
        }

        if(evt.RowsRight < 0)
        {
            return $"{strategy}: {evt.RowsLeft} -> {evt.RowsOut} rows";
        }

        return $"{strategy}: {evt.RowsLeft} x {evt.RowsRight} -> {evt.RowsOut} rows";
    }

    /// <summary>The fixed display name of an evaluation strategy.</summary>
    /// <param name="strategy">The strategy.</param>
    /// <returns>The lowercase strategy token.</returns>
    private static string StrategyName(SparqlExecutionStrategy strategy)
    {
        return strategy switch
        {
            SparqlExecutionStrategy.Columnar => "columnar",
            SparqlExecutionStrategy.Row => "row",
            _ => "streaming"
        };
    }

    /// <summary>The fixed display name of an algebra operator.</summary>
    /// <param name="operator">The operator.</param>
    /// <returns>The operator's name.</returns>
    private static string OperatorName(SparqlExecutionOperator @operator)
    {
        return @operator switch
        {
            SparqlExecutionOperator.Bgp => nameof(SparqlExecutionOperator.Bgp),
            SparqlExecutionOperator.Path => nameof(SparqlExecutionOperator.Path),
            SparqlExecutionOperator.Join => nameof(SparqlExecutionOperator.Join),
            SparqlExecutionOperator.LeftJoin => nameof(SparqlExecutionOperator.LeftJoin),
            SparqlExecutionOperator.Union => nameof(SparqlExecutionOperator.Union),
            SparqlExecutionOperator.Minus => nameof(SparqlExecutionOperator.Minus),
            SparqlExecutionOperator.Filter => nameof(SparqlExecutionOperator.Filter),
            SparqlExecutionOperator.Extend => nameof(SparqlExecutionOperator.Extend),
            SparqlExecutionOperator.Project => nameof(SparqlExecutionOperator.Project),
            SparqlExecutionOperator.Distinct => nameof(SparqlExecutionOperator.Distinct),
            SparqlExecutionOperator.Slice => nameof(SparqlExecutionOperator.Slice),
            SparqlExecutionOperator.OrderBy => nameof(SparqlExecutionOperator.OrderBy),
            SparqlExecutionOperator.Aggregate => nameof(SparqlExecutionOperator.Aggregate),
            SparqlExecutionOperator.Reduced => nameof(SparqlExecutionOperator.Reduced),
            SparqlExecutionOperator.ToList => nameof(SparqlExecutionOperator.ToList),
            SparqlExecutionOperator.ToMultiSet => nameof(SparqlExecutionOperator.ToMultiSet),
            SparqlExecutionOperator.Group => nameof(SparqlExecutionOperator.Group),
            SparqlExecutionOperator.Graph => nameof(SparqlExecutionOperator.Graph),
            SparqlExecutionOperator.Service => nameof(SparqlExecutionOperator.Service),
            SparqlExecutionOperator.Table => nameof(SparqlExecutionOperator.Table),
            _ => nameof(SparqlExecutionOperator.Unit)
        };
    }
}
