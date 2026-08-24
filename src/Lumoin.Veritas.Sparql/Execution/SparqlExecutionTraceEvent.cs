using System;
using System.Diagnostics;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>What a <see cref="SparqlExecutionTraceEvent"/> reports — the kind discriminator of the SPARQL-layer trace union; per-kind payload fields are populated for their kind and defaulted otherwise, the same convention as the Core query trace union.</summary>
public enum SparqlExecutionEventKind
{
    /// <summary>An operator was evaluated; the strategy and row-shape fields carry the payload.</summary>
    OperatorEvaluated = 0,

    /// <summary>A rewrite rule applied (or abstained) at one operator position; <see cref="SparqlExecutionTraceEvent.Label"/> carries the rule name, <see cref="SparqlExecutionTraceEvent.RewriteApplication"/> the verdict, and <see cref="SparqlExecutionTraceEvent.RewritePass"/> the pipeline pass.</summary>
    RewriteApplied = 1,

    /// <summary>An evaluation interception answered or annotated at one operator position; <see cref="SparqlExecutionTraceEvent.Label"/> carries the interception name and <see cref="SparqlExecutionTraceEvent.RowsOut"/> the produced rows (or <c>-1</c> for an annotation).</summary>
    InterceptionApplied = 2
}

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
    Aggregate,

    /// <summary>A duplicate-permission marker (<c>REDUCED</c>).</summary>
    Reduced,

    /// <summary>A sequence coercion (§18.5 ToList).</summary>
    ToList,

    /// <summary>A multiset coercion (§18.5 ToMultiSet).</summary>
    ToMultiSet,

    /// <summary>A grouping (§18.6 Group), distinct from the aggregate join consuming it.</summary>
    Group,

    /// <summary>A <c>GRAPH</c> form.</summary>
    Graph,

    /// <summary>A <c>SERVICE</c> federation step.</summary>
    Service,

    /// <summary>An inline-data table (<c>VALUES</c>).</summary>
    Table,

    /// <summary>The unit table — the join identity.</summary>
    Unit
}

/// <summary>The evaluation strategy an operator took: whether it stayed on the column-major encoded-id island, bridged to the materialized row form, or streamed through the pull-based cursor pipeline.</summary>
public enum SparqlExecutionStrategy
{
    /// <summary>The operator evaluated on encoded-id columns (no decode); its result is a columnar table.</summary>
    Columnar,

    /// <summary>The operator evaluated on materialized rows (its result is row-backed) — either an inherently row operator or a columnar fast path that declined this input's shape.</summary>
    Row,

    /// <summary>The operator streamed as a pull cursor in a compiled pipeline; its event is emitted at pipeline completion (drain or abandon), so <see cref="SparqlExecutionTraceEvent.RowsOut"/> reports the rows it ACTUALLY produced — the early-termination evidence.</summary>
    Streaming
}

/// <summary>
/// A trace event from the SPARQL execution engine — the SPARQL-layer trace union, discriminated by
/// <see cref="Kind"/>: an operator evaluation (which strategy it took and the row shape it operated on), a
/// rewrite-rule application at a position, or an evaluation interception's firing. Per-kind payload fields
/// are populated for their kind and defaulted otherwise, mirroring the Core query trace union's convention.
/// Tests assert the chosen path; the observability surface renders it. Joins by
/// <see cref="CorrelationId"/> with the Core query trace bus's
/// <see cref="Core.Hypertrie.Tracing.QueryTraceEventKind.EngineSelected"/> (the scan-engine choice) for one timeline.
/// </summary>
/// <remarks>
/// A <c>readonly record struct</c> so emission is allocation-free under the <c>in</c>-parameter
/// <see cref="TraceHandler{TEvent}"/>. The strategy is read from the produced table's backing — every columnar fast
/// path yields a columnar table and every row bridge a row-backed one — so the event is faithful without the
/// operators reporting it themselves.
/// </remarks>
[DebuggerDisplay("{Kind} {Operator} {Strategy} in=({RowsLeft},{RowsRight}) out={RowsOut}")]
public readonly record struct SparqlExecutionTraceEvent: ITraceEvent
{
    /// <inheritdoc/>
    public long SequenceNumber { get; init; }

    /// <inheritdoc/>
    public long TimestampTicks { get; init; }

    /// <inheritdoc/>
    public Guid CorrelationId { get; init; }

    /// <summary>The union discriminator; defaults to <see cref="SparqlExecutionEventKind.OperatorEvaluated"/>.</summary>
    public SparqlExecutionEventKind Kind { get; init; }

    /// <summary>The rewrite rule or interception name for the <see cref="SparqlExecutionEventKind.RewriteApplied"/>/<see cref="SparqlExecutionEventKind.InterceptionApplied"/> kinds; <see langword="null"/> for an operator evaluation.</summary>
    public string? Label { get; init; }

    /// <summary>The rule's verdict for the <see cref="SparqlExecutionEventKind.RewriteApplied"/> kind (applied or abstained — not-applicable is never emitted); defaulted otherwise.</summary>
    public AlgebraRewriteApplication RewriteApplication { get; init; }

    /// <summary>The zero-based pipeline pass for the <see cref="SparqlExecutionEventKind.RewriteApplied"/> kind; defaulted otherwise.</summary>
    public int RewritePass { get; init; }

    /// <summary>The operator that was evaluated (or, for a rewrite event, the operator kind of the REPLACED position).</summary>
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

    /// <summary>Constructs a rewrite-application event: one rule applied (or abstained) at one operator position.</summary>
    /// <param name="sequenceNumber">The monotonic sequence number within the evaluation's trace stream.</param>
    /// <param name="timestampTicks">The UTC timestamp in <see cref="DateTime.Ticks"/>.</param>
    /// <param name="correlationId">The evaluation's correlation id.</param>
    /// <param name="ruleName">The rewrite rule's name.</param>
    /// <param name="operator">The operator kind of the replaced (or declined) position.</param>
    /// <param name="application">The rule's verdict — applied or abstained.</param>
    /// <param name="pass">The zero-based pipeline pass.</param>
    /// <returns>The event.</returns>
    public static SparqlExecutionTraceEvent RewriteApplied(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        string ruleName,
        SparqlExecutionOperator @operator,
        AlgebraRewriteApplication application,
        int pass)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = SparqlExecutionEventKind.RewriteApplied,
            Label = ruleName,
            RewriteApplication = application,
            RewritePass = pass,
            Operator = @operator,
            RowsLeft = -1,
            RowsRight = -1,
            RowsOut = -1,
        };
    }

    /// <summary>Constructs an interception-application event: one evaluation interception answered or annotated at one operator position.</summary>
    /// <param name="sequenceNumber">The monotonic sequence number within the evaluation's trace stream.</param>
    /// <param name="timestampTicks">The UTC timestamp in <see cref="DateTime.Ticks"/>.</param>
    /// <param name="correlationId">The evaluation's correlation id.</param>
    /// <param name="interceptionName">The interception's name.</param>
    /// <param name="operator">The operator kind of the intercepted position.</param>
    /// <param name="rows">The rows the interception produced, or <c>-1</c> for an annotation.</param>
    /// <returns>The event.</returns>
    public static SparqlExecutionTraceEvent InterceptionApplied(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        string interceptionName,
        SparqlExecutionOperator @operator,
        int rows)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = SparqlExecutionEventKind.InterceptionApplied,
            Label = interceptionName,
            Operator = @operator,
            RowsLeft = -1,
            RowsRight = -1,
            RowsOut = rows,
        };
    }
}
