using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>An inline <c>VALUES</c> block as a cursor: its rows in block order, built once at compile time.</summary>
internal sealed class TableCursor : SolutionCursor
{
    private readonly IReadOnlyList<SparqlSolution> rows;

    private int index;

    private SparqlSolution? current;

    /// <summary>Constructs the cursor over the block's already-built solution rows.</summary>
    /// <param name="rows">The block's solutions, in block order.</param>
    public TableCursor(IReadOnlyList<SparqlSolution> rows)
    {
        this.rows = rows;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => current!;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if(index >= rows.Count)
        {
            return ValueTask.FromResult(false);
        }

        current = rows[index];
        index++;
        RowsProduced++;

        return ValueTask.FromResult(true);
    }

    /// <inheritdoc/>
    public override ValueTask ResetAsync(SparqlSolution preBinding)
    {
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
