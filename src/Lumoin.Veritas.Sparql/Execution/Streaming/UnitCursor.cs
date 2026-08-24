using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>The <c>UnitTable</c> identity as a cursor: exactly one empty solution, then exhaustion.</summary>
internal sealed class UnitCursor : SolutionCursor
{
    /// <summary>The single empty solution the unit table yields.</summary>
    private static SparqlSolution EmptySolution { get; } = new([]);

    private bool emitted;

    /// <inheritdoc/>
    public override SparqlSolution Current => EmptySolution;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if(emitted)
        {
            return ValueTask.FromResult(false);
        }

        emitted = true;
        RowsProduced++;

        return ValueTask.FromResult(true);
    }

    /// <inheritdoc/>
    public override ValueTask ResetAsync(SparqlSolution preBinding)
    {
        emitted = false;
        RowsProduced = 0;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
