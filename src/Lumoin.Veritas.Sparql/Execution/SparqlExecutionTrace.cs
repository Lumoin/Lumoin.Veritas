using System;
using System.Threading;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The evaluation-scoped sink the engine emits <see cref="SparqlExecutionTraceEvent"/>s to: it holds the handler,
/// the evaluation's correlation id, and a per-evaluation sequence counter. Created once per
/// <see cref="SparqlQueryEngine"/> evaluation (never engine state — evaluations may run concurrently), so the
/// sequence is monotonic within one query without racing another. When no handler is wired, every emit is a cheap
/// no-op, so the trace points cost a null check on the untraced path.
/// </summary>
internal sealed class SparqlExecutionTrace
{
    private readonly TraceHandler<SparqlExecutionTraceEvent>? handler;

    private readonly Guid correlationId;

    private readonly TimeProvider timeProvider;

    //A counter incremented per emit; a field rather than a property because Interlocked needs a ref.
    private long sequence;

    /// <summary>Constructs a trace sink over the (optional) handler.</summary>
    /// <param name="handler">The handler events are emitted to, or <see langword="null"/> to disable tracing.</param>
    /// <param name="correlationId">The correlation id stamped on every event of this evaluation.</param>
    /// <param name="timeProvider">The clock the event timestamps are read from.</param>
    public SparqlExecutionTrace(TraceHandler<SparqlExecutionTraceEvent>? handler, Guid correlationId, TimeProvider timeProvider)
    {
        this.handler = handler;
        this.correlationId = correlationId;
        this.timeProvider = timeProvider;
    }

    /// <summary>Whether a handler is wired; lets a caller skip building emit arguments when tracing is off.</summary>
    public bool IsEnabled => handler is not null;

    /// <summary>
    /// Emits one operator-evaluated event, reading the strategy from <paramref name="result"/>'s backing — a
    /// columnar table means the columnar fast path ran, a row-backed one means the operator bridged to (or is
    /// inherently) the row form.
    /// </summary>
    /// <param name="operator">The operator evaluated.</param>
    /// <param name="result">The operator's result table; its backing reports the strategy and its count the output rows.</param>
    /// <param name="rowsLeft">The left/only input row count, or <c>-1</c> for a leaf.</param>
    /// <param name="rowsRight">The right input row count, or <c>-1</c> for a leaf or unary operator.</param>
    public void Emit(SparqlExecutionOperator @operator, SolutionTable result, int rowsLeft, int rowsRight)
    {
        if(handler is null)
        {
            return;
        }

        SparqlExecutionStrategy strategy = result.IsColumnar ? SparqlExecutionStrategy.Columnar : SparqlExecutionStrategy.Row;
        long next = Interlocked.Increment(ref sequence);
        SparqlExecutionTraceEvent evt = SparqlExecutionTraceEvent.OperatorEvaluated(
            next, timeProvider.GetUtcNow().UtcTicks, correlationId, @operator, strategy, rowsLeft, rowsRight, result.Count);

        handler(in evt);
    }

    /// <summary>
    /// Emits one streamed-operator event with explicit row counts — the pipeline-completion form: a
    /// streamed cursor has no materialised result table to read a strategy or count from, so its
    /// <see cref="SparqlExecutionStrategy.Streaming"/> event carries the rows it ACTUALLY produced (drain or
    /// abandon), in the same correlation and sequence stream as the evaluation that spawned the pipeline.
    /// </summary>
    /// <param name="operator">The streamed operator.</param>
    /// <param name="rowsLeft">The (left/input) child cursor's produced rows, or <c>-1</c> for a leaf.</param>
    /// <param name="rowsRight">The right child cursor's produced rows, or <c>-1</c> for a leaf or unary operator.</param>
    /// <param name="rowsOut">The rows the cursor produced.</param>
    public void EmitStreaming(SparqlExecutionOperator @operator, int rowsLeft, int rowsRight, int rowsOut)
    {
        if(handler is null)
        {
            return;
        }

        long next = Interlocked.Increment(ref sequence);
        SparqlExecutionTraceEvent evt = SparqlExecutionTraceEvent.OperatorEvaluated(
            next, timeProvider.GetUtcNow().UtcTicks, correlationId, @operator, SparqlExecutionStrategy.Streaming, rowsLeft, rowsRight, rowsOut);

        handler(in evt);
    }

    /// <summary>
    /// Emits one rewrite-application event — a rule applied (or abstained) at one operator position, in the
    /// same correlation and sequence stream as the evaluation the pipeline ran for.
    /// </summary>
    /// <param name="ruleName">The rewrite rule's name.</param>
    /// <param name="operator">The operator kind of the replaced (or declined) position.</param>
    /// <param name="application">The rule's verdict — applied or abstained.</param>
    /// <param name="pass">The zero-based pipeline pass.</param>
    public void EmitRewrite(string ruleName, SparqlExecutionOperator @operator, AlgebraRewriteApplication application, int pass)
    {
        if(handler is null)
        {
            return;
        }

        long next = Interlocked.Increment(ref sequence);
        SparqlExecutionTraceEvent evt = SparqlExecutionTraceEvent.RewriteApplied(
            next, timeProvider.GetUtcNow().UtcTicks, correlationId, ruleName, @operator, application, pass);

        handler(in evt);
    }

    /// <summary>
    /// Emits one interception-application event — a fast-path entry answered or annotated at one operator
    /// position, in the same correlation and sequence stream as the evaluation that consulted it.
    /// </summary>
    /// <param name="interceptionName">The interception entry's name.</param>
    /// <param name="operator">The operator kind of the intercepted position.</param>
    /// <param name="rows">The rows the interception produced, or <c>-1</c> for an annotation.</param>
    public void EmitInterception(string interceptionName, SparqlExecutionOperator @operator, int rows)
    {
        if(handler is null)
        {
            return;
        }

        long next = Interlocked.Increment(ref sequence);
        SparqlExecutionTraceEvent evt = SparqlExecutionTraceEvent.InterceptionApplied(
            next, timeProvider.GetUtcNow().UtcTicks, correlationId, interceptionName, @operator, rows);

        handler(in evt);
    }
}
