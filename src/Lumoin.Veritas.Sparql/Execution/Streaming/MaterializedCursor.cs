using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The materialise-boundary bridge: wraps a <see cref="SolutionTable"/> as a cursor source. In the lazy
/// configuration the boundary holds a non-streamable subtree and evaluates it through the materialising
/// executor on FIRST pull, so an early-terminating consumer never pays for a boundary it never reaches — that
/// first pull is arbitrarily expensive and async-deep by design, and its exception/cancellation path is covered
/// by the consumer's try/finally disposal contract. The evaluated table is binding-independent, so
/// <see cref="ResetAsync"/> retains it and re-reads from the first row.
/// </summary>
internal sealed class MaterializedCursor : SolutionCursor
{
    private readonly SparqlQueryEngine? engine;

    private readonly AlgebraOperator? subtree;

    private readonly TermId activeGraph;

    private readonly CursorBudget? budget;

    private readonly int existsDepth;

    private readonly SparqlExecutionTrace? trace;

    private SolutionTable? table;

    private IReadOnlyList<SparqlSolution>? rows;

    private int index;

    private SparqlSolution? current;

    /// <summary>Wraps an already-materialised table.</summary>
    /// <param name="table">The table whose rows the cursor yields, in table row order.</param>
    public MaterializedCursor(SolutionTable table)
    {
        this.table = table;
    }

    /// <summary>Wraps a non-streamable subtree, evaluated through the materialising executor on first pull.</summary>
    /// <param name="engine">The engine whose materialising driver evaluates the subtree.</param>
    /// <param name="subtree">The non-streamable algebra subtree.</param>
    /// <param name="activeGraph">The active graph the subtree evaluates under, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="budget">The evaluation's shared cursor-budget cell; the first-pull driver re-entry draws its nested pipelines from it, so the budget remaining at this boundary's position carries through by identity.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at this boundary's position.</param>
    /// <param name="trace">The spawning evaluation's trace sink the first-pull re-entry emits into, or <see langword="null"/> when the pipeline compiled without one.</param>
    public MaterializedCursor(SparqlQueryEngine engine, AlgebraOperator subtree, TermId activeGraph, CursorBudget budget, int existsDepth, SparqlExecutionTrace? trace)
    {
        this.engine = engine;
        this.subtree = subtree;
        this.activeGraph = activeGraph;
        this.budget = budget;
        this.existsDepth = existsDepth;
        this.trace = trace;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => current!;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if(rows is null)
        {
            table ??= await engine!.EvaluateBoundaryAsync(subtree!, activeGraph, trace, budget!, existsDepth, cancellationToken).ConfigureAwait(false);
            rows = table.AsRows();
        }

        if(index >= rows.Count)
        {
            return false;
        }

        current = rows[index];
        index++;
        RowsProduced++;

        return true;
    }

    /// <inheritdoc/>
    public override ValueTask ResetAsync(SparqlSolution preBinding)
    {
        //The materialised table (lazy or eager) is binding-independent state, retained across re-arms per the
        //cursor contract; only the read position and the per-binding counters re-arm.
        index = 0;
        current = null;
        RowsProduced = 0;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        current = null;

        return ValueTask.CompletedTask;
    }
}
