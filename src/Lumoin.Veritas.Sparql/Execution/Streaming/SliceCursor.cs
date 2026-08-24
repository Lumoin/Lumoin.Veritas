using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming <c>OFFSET</c>/<c>LIMIT</c> window — THE filter-aware cap: skips the offset, emits up to the
/// limit, and STOPS PULLING its input once the window fills, so upstream production terminates early.
/// Compiled only over an order-preserving chain, which makes its window row-for-row identical
/// to the materialised path's positional window.
/// </summary>
internal sealed class SliceCursor : SolutionCursor
{
    private readonly SolutionCursor input;

    private readonly int offset;

    private readonly int? limit;

    private int skipped;

    private int emitted;

    /// <summary>Constructs the cursor over its input child.</summary>
    /// <param name="input">The input cursor.</param>
    /// <param name="offset">The rows to skip.</param>
    /// <param name="limit">The window size, or <see langword="null"/> for no limit.</param>
    public SliceCursor(SolutionCursor input, int offset, int? limit)
    {
        this.input = input;
        this.offset = offset;
        this.limit = limit;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => input.Current;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if(limit is int cap && emitted >= cap)
        {
            return false;
        }

        while(skipped < offset)
        {
            if(!await input.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            skipped++;
        }

        if(!await input.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        emitted++;
        RowsProduced++;

        return true;
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        await input.ResetAsync(preBinding).ConfigureAwait(false);
        skipped = 0;
        emitted = 0;
        RowsProduced = 0;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
