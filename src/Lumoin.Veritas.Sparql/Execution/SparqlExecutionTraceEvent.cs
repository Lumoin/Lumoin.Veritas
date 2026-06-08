using System;
using System.Diagnostics;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>The algebra operator a <see cref="SparqlExecutionTraceEvent"/> reports the evaluation of.</summary>
public enum SparqlExecutionOperator
{
    /// <summary>A basic graph pattern leaf.</summary>
    Bgp,

    /// <summary>A property-path leaf.</summary>
    Path,

    /// <summary>A join (§18.6 Join).</summary>
    Join,

    /// <summary>A left join / <c>OPTIONAL</c> (§18.6 LeftJoin).</summary>
    LeftJoin,

    /// <summary>A union (§18.6 Union).</summary>
    Union,

    /// <summary>A minus (§18.6 Minus).</summary>
    Minus,

    /// <summary>A filter (§18.6 Filter).</summary>
    Filter,

    /// <summary>An extend / <c>BIND</c> (§18.6 Extend).</summary>
    Extend,

    /// <summary>A projection.</summary>
    Project,

    /// <summary>A duplicate elimination (<c>DISTINCT</c>).</summary>
    Distinct,

    /// <summary>An <c>OFFSET</c>/<c>LIMIT</c> window.</summary>
    Slice,

    /// <summary>An <c>ORDER BY</c>.</summary>
    OrderBy,

    /// <summary>A grouping aggregate (<c>GROUP BY</c> + aggregates).</summary>
    Aggregate
}

/// <summary>The evaluation strategy an operator took: whether it stayed on the column-major encoded-id island or bridged to the materialized row form.</summary>
public enum SparqlExecutionStrategy
{
    /// <summary>The operator evaluated on encoded-id columns (no decode); its result is a columnar table.</summary>
    Columnar,

    /// <summary>The operator evaluated on materialized rows (its result is row-backed) — either an inherently row operator or a columnar fast path that declined this input's shape.</summary>
    Row
}

/// <summary>
/// A trace event from the SPARQL execution engine recording, per algebra operator, which evaluation strategy it
/// took (column-major island vs materialized rows) and the row shape it operated on — the side channel for "what
/// was selected, on how much data". Tests assert the chosen path; the observability surface renders it. Joins by
/// <see cref="CorrelationId"/> with the Core query trace bus's
/// <see cref="Core.Hypertrie.Tracing.QueryTraceEventKind.EngineSelected"/> (the scan-engine choice) for one timeline.
/// </summary>
/// <remarks>
/// A <c>readonly record struct</c> so emission is allocation-free under the <c>in</c>-parameter
/// <see cref="TraceHandler{TEvent}"/>. The strategy is read from the produced table's backing — every columnar fast
/// path yields a columnar table and every row bridge a row-backed one — so the event is faithful without the
/// operators reporting it themselves.
/// </remarks>
[DebuggerDisplay("{Operator} {Strategy} in=({RowsLeft},{RowsRight}) out={RowsOut}")]
public readonly record struct SparqlExecutionTraceEvent: ITraceEvent
{
    /// <inheritdoc/>
    public long SequenceNumber { get; init; }

    /// <inheritdoc/>
    public long TimestampTicks { get; init; }

    /// <inheritdoc/>
    public Guid CorrelationId { get; init; }

    /// <summary>The operator that was evaluated.</summary>
    public SparqlExecutionOperator Operator { get; init; }

    /// <summary>The strategy the operator took.</summary>
    public SparqlExecutionStrategy Strategy { get; init; }

    /// <summary>The row count of the (left/only) input, or <c>-1</c> when the operator is a leaf with no table input.</summary>
    public int RowsLeft { get; init; }

    /// <summary>The row count of the right input for a binary operator, or <c>-1</c> for a leaf or unary operator.</summary>
    public int RowsRight { get; init; }

    /// <summary>The operator's output row count.</summary>
    public int RowsOut { get; init; }

    /// <summary>Constructs an operator-evaluated event.</summary>
    /// <param name="sequenceNumber">The monotonic sequence number within the evaluation's trace stream.</param>
    /// <param name="timestampTicks">The UTC timestamp in <see cref="DateTime.Ticks"/>.</param>
    /// <param name="correlationId">The evaluation's correlation id.</param>
    /// <param name="operator">The operator evaluated.</param>
    /// <param name="strategy">The strategy taken.</param>
    /// <param name="rowsLeft">The left/only input row count, or <c>-1</c> for a leaf.</param>
    /// <param name="rowsRight">The right input row count, or <c>-1</c> for a leaf or unary operator.</param>
    /// <param name="rowsOut">The output row count.</param>
    /// <returns>The event.</returns>
    public static SparqlExecutionTraceEvent OperatorEvaluated(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        SparqlExecutionOperator @operator,
        SparqlExecutionStrategy strategy,
        int rowsLeft,
        int rowsRight,
        int rowsOut)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Operator = @operator,
            Strategy = strategy,
            RowsLeft = rowsLeft,
            RowsRight = rowsRight,
            RowsOut = rowsOut,
        };
    }
}
