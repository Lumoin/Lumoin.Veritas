using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>The streaming <c>UNION</c>: the left child drained first, then the right — sequential, matching the materialised left-before-right concatenation, so the cursor is order-preserving and duplicates flow through (bag semantics).</summary>
internal sealed class UnionCursor : SolutionCursor
{
    private readonly SolutionCursor left;

    private readonly SolutionCursor right;

    private bool leftExhausted;

    /// <summary>Constructs the cursor over its children.</summary>
    /// <param name="left">The left child.</param>
    /// <param name="right">The right child.</param>
    public UnionCursor(SolutionCursor left, SolutionCursor right)
    {
        this.left = left;
        this.right = right;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => leftExhausted ? right.Current : left.Current;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if(!leftExhausted)
        {
            if(await left.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                RowsProduced++;

                return true;
            }

            leftExhausted = true;
        }

        if(await right.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            RowsProduced++;

            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        await left.ResetAsync(preBinding).ConfigureAwait(false);
        await right.ResetAsync(preBinding).ConfigureAwait(false);
        leftExhausted = false;
        RowsProduced = 0;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
