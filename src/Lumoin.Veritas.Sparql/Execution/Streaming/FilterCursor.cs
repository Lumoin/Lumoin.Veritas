using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming <c>FILTER</c>: per pulled row, resolves any <c>EXISTS</c>/<c>NOT EXISTS</c> through the
/// owning pipeline's compile-once plan registry (composing the two headline wins) and keeps the rows whose
/// condition has effective boolean value true — the materialising row path's semantics, one row at a time.
/// </summary>
internal sealed class FilterCursor : SolutionCursor
{
    private readonly SparqlQueryEngine engine;

    private readonly SolutionCursor input;

    private readonly ExpressionNode condition;

    private readonly bool conditionHasExists;

    private readonly TermId activeGraph;

    private readonly ExistsRegistry existsRegistry;

    private readonly CursorBudget cursorBudget;

    private readonly int existsDepth;

    /// <summary>Constructs the cursor over its input child.</summary>
    /// <param name="engine">The engine whose expression machinery evaluates the condition.</param>
    /// <param name="input">The input cursor.</param>
    /// <param name="condition">The filter condition.</param>
    /// <param name="activeGraph">The active graph any EXISTS in the condition re-enters in.</param>
    /// <param name="existsRegistry">The owning pipeline's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at the pipeline's position.</param>
    public FilterCursor(SparqlQueryEngine engine, SolutionCursor input, ExpressionNode condition, TermId activeGraph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth)
    {
        this.engine = engine;
        this.input = input;
        this.condition = condition;
        conditionHasExists = SparqlQueryEngine.ContainsExists(condition);
        this.activeGraph = activeGraph;
        this.existsRegistry = existsRegistry;
        this.cursorBudget = cursorBudget;
        this.existsDepth = existsDepth;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => input.Current;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        while(await input.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            SparqlSolution solution = input.Current;
            ExpressionNode resolved = conditionHasExists
                ? await engine.ResolveExistsForPipelineAsync(condition, solution, activeGraph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false)
                : condition;
            if(SparqlExpressionEvaluator.Satisfies(resolved, solution, engine.ExpressionContext))
            {
                RowsProduced++;

                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        await input.ResetAsync(preBinding).ConfigureAwait(false);
        RowsProduced = 0;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
