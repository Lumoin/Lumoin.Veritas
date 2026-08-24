using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming <c>MINUS</c> (§18.6) with the definitional semantics: the RIGHT (subtracting) child drains
/// once into the materialised build, the LEFT streams (order-preserving w.r.t. the left); a streamed left
/// row is removed only when some right row BOTH shares a variable with it AND is compatible — the
/// shared-variable requirement is the §18.6 disjoint-domain exception: a left row sharing NO variable with
/// any right row is KEPT even though disjoint mappings are technically compatible. The hash index (over the
/// variables every build row binds) is a candidate filter whose hits are verified with the full
/// per-pair predicate, and a probe that does not bind the key falls to the nested-loop scan — mirroring the
/// materialised path's hash-eligibility-or-nested-loop split with per-candidate verification.
/// </summary>
internal sealed class MinusCursor : SolutionCursor
{
    private readonly SolutionCursor left;

    private readonly SolutionCursor right;

    private MaterialisedBuild? build;

    private SparqlSolution? current;

    /// <summary>Constructs the cursor over its children (build right, stream left).</summary>
    /// <param name="left">The streamed (kept) child.</param>
    /// <param name="right">The drained subtracting child.</param>
    public MinusCursor(SolutionCursor left, SolutionCursor right)
    {
        this.left = left;
        this.right = right;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => current!;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        build ??= await MaterialisedBuild.DrainAsync(right, cancellationToken).ConfigureAwait(false);

        while(await left.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            SparqlSolution candidate = left.Current;
            if(!IsRemoved(candidate))
            {
                current = candidate;
                RowsProduced++;

                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a left row is removed: some build row shares a variable with it AND is compatible (index candidates verified with the same full predicate).</summary>
    /// <param name="candidate">The streamed left row.</param>
    /// <returns><see langword="true"/> when a subtracting row applies.</returns>
    private bool IsRemoved(SparqlSolution candidate)
    {
        if(build!.Rows.Count == 0)
        {
            return false;
        }

        if(build.BindsKey(candidate))
        {
            for(int rowId = build.Index!.FirstMatch(candidate); rowId >= 0; rowId = build.Index.NextMatch(rowId))
            {
                SparqlSolution subtractor = build.Index.RowAt(rowId);
                if(SparqlQueryEngine.SharesVariable(candidate, subtractor) && SparqlQueryEngine.AreCompatible(candidate, subtractor))
                {
                    return true;
                }
            }

            //Index candidates share (and agree on) the whole key by construction; a build row NOT equal on
            //the key cannot be compatible with a probe that binds it, so no scan remainder exists here.
            return false;
        }

        foreach(SparqlSolution subtractor in build.Rows)
        {
            if(SparqlQueryEngine.SharesVariable(candidate, subtractor) && SparqlQueryEngine.AreCompatible(candidate, subtractor))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        //The drained build is binding-independent and retained; only the streamed side re-arms.
        await left.ResetAsync(preBinding).ConfigureAwait(false);
        current = null;
        RowsProduced = 0;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        current = null;
        build = null;

        return ValueTask.CompletedTask;
    }
}
