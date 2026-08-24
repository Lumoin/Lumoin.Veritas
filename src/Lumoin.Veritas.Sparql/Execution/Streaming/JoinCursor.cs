using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming <c>JOIN</c> (§18.6): the RIGHT child drains once into a materialised, optionally hash-keyed
/// build (binding-independent — retained across re-arms), and the LEFT streams as the probe; each probe row
/// emits its merge with every COMPATIBLE build row (index candidates are verified with the full
/// compatibility check, so the emitted multiset equals the definitional join on every input shape). NOT
/// order-preserving: the emission order follows the streamed probe, which no materialised join
/// order matches — the windowed-slice gate treats this cursor as the reordering exception.
/// </summary>
internal sealed class JoinCursor : SolutionCursor
{
    private readonly SolutionCursor left;

    private readonly SolutionCursor right;

    private MaterialisedBuild? build;

    private SparqlSolution? currentLeft;

    private bool probeViaIndex;

    private int indexRowId = -1;

    private int scanPosition;

    private SparqlSolution? current;

    /// <summary>Constructs the cursor over its children (build right, probe left — the committed v1 heuristic).</summary>
    /// <param name="left">The streamed probe child.</param>
    /// <param name="right">The drained build child.</param>
    public JoinCursor(SolutionCursor left, SolutionCursor right)
    {
        this.left = left;
        this.right = right;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => current!;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => false;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        build ??= await MaterialisedBuild.DrainAsync(right, cancellationToken).ConfigureAwait(false);
        if(build.Rows.Count == 0)
        {
            return false;
        }

        while(true)
        {
            if(currentLeft is null)
            {
                if(!await left.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                currentLeft = left.Current;
                probeViaIndex = build.BindsKey(currentLeft);
                indexRowId = probeViaIndex ? build.Index!.FirstMatch(currentLeft) : -1;
                scanPosition = 0;
            }

            if(probeViaIndex)
            {
                while(indexRowId >= 0)
                {
                    SparqlSolution candidate = build.Index!.RowAt(indexRowId);
                    indexRowId = build.Index.NextMatch(indexRowId);
                    if(SparqlQueryEngine.AreCompatible(currentLeft, candidate))
                    {
                        current = SparqlQueryEngine.Merge(currentLeft, candidate);
                        RowsProduced++;

                        return true;
                    }
                }
            }
            else
            {
                while(scanPosition < build.Rows.Count)
                {
                    SparqlSolution candidate = build.Rows[scanPosition];
                    scanPosition++;
                    if(SparqlQueryEngine.AreCompatible(currentLeft, candidate))
                    {
                        current = SparqlQueryEngine.Merge(currentLeft, candidate);
                        RowsProduced++;

                        return true;
                    }
                }
            }

            currentLeft = null;
        }
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        //The drained build is binding-independent (materialised rows, never a live enumerator) and is
        //retained; only the streamed probe side re-arms.
        await left.ResetAsync(preBinding).ConfigureAwait(false);
        currentLeft = null;
        current = null;
        indexRowId = -1;
        scanPosition = 0;
        RowsProduced = 0;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        current = null;
        currentLeft = null;
        build = null;

        return ValueTask.CompletedTask;
    }
}
