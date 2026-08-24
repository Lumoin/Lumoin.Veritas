using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The structural pass-throughs as one cursor: <c>REDUCED</c> (permits but does not require duplicate
/// elimination — passing rows through is conformant, matching the materialised path), and the
/// <c>ToList</c>/<c>ToMultiSet</c> sequence/multiset conversions (identity with no <c>ORDER BY</c> in
/// scope, matching the materialised combine). Rows flow through unchanged and in order.
/// </summary>
internal sealed class PassThroughCursor : SolutionCursor
{
    private readonly SolutionCursor input;

    /// <summary>Constructs the cursor over its input child.</summary>
    /// <param name="input">The input cursor.</param>
    public PassThroughCursor(SolutionCursor input)
    {
        this.input = input;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => input.Current;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if(!await input.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        RowsProduced++;

        return true;
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
