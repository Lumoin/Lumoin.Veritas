using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Parallel worst-case-optimal join over the columnar index by
/// HyperCube partitioning: the degree of parallelism factors into
/// per-variable share counts, every cell of the resulting grid runs
/// its own evaluator over the shared immutable index restricted to
/// the keys it owns, and the cells' solution streams merge into one
/// output. Each output tuple belongs to exactly one cell, so the
/// merge needs no deduplication and the workers share nothing but
/// the read-only index.
/// </summary>
/// <remarks>
/// <para>
/// <b>Share planning.</b> The degree of parallelism is split into
/// at most two factors assigned to the first two variables of the
/// query's global order — a balanced grid rather than a single
/// partitioned variable, which is the skew HyperCube partitioning
/// exists to avoid. Cost-modelled share optimisation over relation
/// statistics is a later refinement.
/// </para>
/// <para>
/// <b>Degenerate cases.</b> A degree of parallelism of one, or a
/// query with no variables, runs the sequential evaluator
/// directly.
/// </para>
/// <para>
/// <b>Ordering.</b> Solutions arrive in cell-completion order —
/// the contract is the same driver-determined order every
/// evaluator already carries, not a stable one.
/// </para>
/// </remarks>
public static class ColumnarHyperCube
{
    /// <summary>
    /// Evaluates the query over <paramref name="index"/> with
    /// <paramref name="degreeOfParallelism"/> HyperCube cells.
    /// </summary>
    /// <param name="index">The columnar index to query.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="degreeOfParallelism">The number of cells to run concurrently; one runs sequentially.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events.</param>
    /// <param name="planner">The planner to use, or <c>null</c> to use <see cref="Planners.FirstOccurrence"/>.</param>
    /// <param name="accessControl">Optional access-control policy, consulted by every cell; the delegate must tolerate concurrent calls.</param>
    /// <param name="accessContext">Caller-supplied access context. Required when <paramref name="accessControl"/> is non-<c>null</c>.</param>
    /// <param name="traceHandler">Optional trace handler; cells emit their own driver-level events under the shared correlation id, and the handler must tolerate concurrent calls.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">Cancellation token threaded into every cell.</param>
    /// <returns>An async sequence of solutions, in cell-completion order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="degreeOfParallelism"/> is less than one.</exception>
    public static IAsyncEnumerable<Solution> Query(
        ColumnarTripleIndex index,
        BasicGraphPattern query,
        int degreeOfParallelism,
        TimeProvider timeProvider,
        Planner? planner = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(degreeOfParallelism, 1);

        if(degreeOfParallelism == 1 || query.Variables.Count == 0)
        {
            ColumnarBasicGraphPatternEvaluator sequential = new(
                index, query, planner ?? Planners.FirstOccurrence(query), timeProvider,
                accessControl, accessContext, traceHandler, correlationId);

            return sequential.EvaluateAsync(cancellationToken);
        }

        return QueryCore(
            index, query, degreeOfParallelism, timeProvider,
            planner, accessControl, accessContext, traceHandler, correlationId, cancellationToken);
    }

    private static async IAsyncEnumerable<Solution> QueryCore(
        ColumnarTripleIndex index,
        BasicGraphPattern query,
        int degreeOfParallelism,
        TimeProvider timeProvider,
        Planner? planner,
        AccessControlDelegate? accessControl,
        AccessContext? accessContext,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int[] shares = PlanShares(degreeOfParallelism, query.Variables.Count);

        Channel<Solution> channel = Channel.CreateBounded<Solution>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
        });

        Task workers = RunCellsAsync(
            index, query, shares, timeProvider,
            planner, accessControl, accessContext, traceHandler, correlationId,
            channel.Writer, cancellationToken);

        await foreach(Solution solution in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return solution;
        }

        //Surface any cell failure to the consumer after the channel
        //drains; completion errors already faulted the reader above.
        await workers.ConfigureAwait(false);
    }

    //Runs one evaluator per cell of the share grid, each writing
    //its solutions into the shared channel; completes the channel
    //when every cell finishes (or faults it with the first error).
    private static async Task RunCellsAsync(
        ColumnarTripleIndex index,
        BasicGraphPattern query,
        int[] shares,
        TimeProvider timeProvider,
        Planner? planner,
        AccessControlDelegate? accessControl,
        AccessContext? accessContext,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        ChannelWriter<Solution> writer,
        CancellationToken cancellationToken)
    {
        int cellCount = 1;

        for(int i = 0; i < shares.Length; i++)
        {
            cellCount *= shares[i];
        }

        List<Task> cells = new(cellCount);

        for(int cellIndex = 0; cellIndex < cellCount; cellIndex++)
        {
            int[] coordinates = CoordinatesOf(cellIndex, shares);
            HyperCubeCell cell = new(shares, coordinates);

            cells.Add(Task.Run(
                async () =>
                {
                    ColumnarBasicGraphPatternEvaluator evaluator = new(
                        index, query, planner ?? Planners.FirstOccurrence(query), timeProvider,
                        accessControl, accessContext, traceHandler, correlationId, cell);

                    await foreach(Solution solution in evaluator.EvaluateAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await writer.WriteAsync(solution, cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken));
        }

        try
        {
            await Task.WhenAll(cells).ConfigureAwait(false);
            writer.Complete();
        }
        catch(Exception exception)
        {
            writer.Complete(exception);

            throw;
        }
    }

    //Splits the degree of parallelism into at most two factors over
    //the first variables of the global order — the most balanced
    //factor pair, so the grid stays close to square.
    private static int[] PlanShares(int degreeOfParallelism, int variableCount)
    {
        int[] shares = new int[variableCount];

        for(int i = 0; i < shares.Length; i++)
        {
            shares[i] = 1;
        }

        if(variableCount == 1)
        {
            shares[0] = degreeOfParallelism;

            return shares;
        }

        int second = 1;

        for(int candidate = 2; candidate * candidate <= degreeOfParallelism; candidate++)
        {
            if(degreeOfParallelism % candidate == 0)
            {
                second = candidate;
            }
        }

        shares[0] = degreeOfParallelism / second;
        shares[1] = second;

        return shares;
    }

    //Decomposes a linear cell index into per-variable coordinates
    //over the share grid.
    private static int[] CoordinatesOf(int cellIndex, int[] shares)
    {
        int[] coordinates = new int[shares.Length];
        int remaining = cellIndex;

        for(int i = 0; i < shares.Length; i++)
        {
            coordinates[i] = remaining % shares[i];
            remaining /= shares[i];
        }

        return coordinates;
    }
}
