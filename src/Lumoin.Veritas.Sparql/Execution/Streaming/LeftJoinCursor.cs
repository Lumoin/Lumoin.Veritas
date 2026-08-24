using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming <c>OPTIONAL</c> (§18.6 LeftJoin) with the definitional semantics: the RIGHT child drains once
/// into the materialised build, the LEFT streams as the outer side (order-preserving w.r.t. the left — both
/// modes iterate left as the outer side); for each streamed left row, EVERY compatible build row's merge is
/// evaluated against the condition (including <c>EXISTS</c> through the owning pipeline's plan registry) and
/// every condition-SATISFYING extension is emitted; the left row alone is emitted ONLY when no compatible
/// right produced a satisfying merge — decided per left row AFTER all compatible rights are examined (a
/// compatible-but-condition-failing right does NOT suppress the bare left row).
/// </summary>
internal sealed class LeftJoinCursor : SolutionCursor
{
    private readonly SparqlQueryEngine engine;

    private readonly SolutionCursor left;

    private readonly SolutionCursor right;

    private readonly ExpressionNode? condition;

    private readonly bool conditionHasExists;

    private readonly TermId activeGraph;

    private readonly ExistsRegistry existsRegistry;

    private readonly CursorBudget cursorBudget;

    private readonly int existsDepth;

    private MaterialisedBuild? build;

    private SparqlSolution? currentLeft;

    private bool probeViaIndex;

    private int indexRowId = -1;

    private int scanPosition;

    private bool anySatisfied;

    private SparqlSolution? current;

    /// <summary>Constructs the cursor over its children (build right, stream left).</summary>
    /// <param name="engine">The engine whose expression machinery evaluates the condition.</param>
    /// <param name="left">The streamed (outer, kept) child.</param>
    /// <param name="right">The drained optional child.</param>
    /// <param name="condition">The optional join condition (the lifted inner <c>FILTER</c>), or <see langword="null"/>.</param>
    /// <param name="activeGraph">The active graph any EXISTS in the condition re-enters in.</param>
    /// <param name="existsRegistry">The owning pipeline's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at the pipeline's position.</param>
    public LeftJoinCursor(SparqlQueryEngine engine, SolutionCursor left, SolutionCursor right, ExpressionNode? condition, TermId activeGraph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth)
    {
        this.engine = engine;
        this.left = left;
        this.right = right;
        this.condition = condition;
        conditionHasExists = condition is not null && SparqlQueryEngine.ContainsExists(condition);
        this.activeGraph = activeGraph;
        this.existsRegistry = existsRegistry;
        this.cursorBudget = cursorBudget;
        this.existsDepth = existsDepth;
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => current!;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        build ??= await MaterialisedBuild.DrainAsync(right, cancellationToken).ConfigureAwait(false);

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
                anySatisfied = false;
            }

            while(TryTakeCandidate(out SparqlSolution candidate))
            {
                if(!SparqlQueryEngine.AreCompatible(currentLeft, candidate))
                {
                    continue;
                }

                SparqlSolution merged = SparqlQueryEngine.Merge(currentLeft, candidate);
                if(await SatisfiesConditionAsync(merged, cancellationToken).ConfigureAwait(false))
                {
                    anySatisfied = true;
                    current = merged;
                    RowsProduced++;

                    return true;
                }
            }

            //All compatible rights examined for this left row: emit it alone only when none satisfied.
            SparqlSolution outer = currentLeft;
            currentLeft = null;
            if(!anySatisfied)
            {
                current = outer;
                RowsProduced++;

                return true;
            }
        }
    }

    /// <summary>Takes the next build candidate for the current left row (index-filtered when the probe binds the key, else the full scan).</summary>
    /// <param name="candidate">Receives the candidate row.</param>
    /// <returns><see langword="true"/> while candidates remain.</returns>
    private bool TryTakeCandidate(out SparqlSolution candidate)
    {
        if(probeViaIndex)
        {
            if(indexRowId >= 0)
            {
                candidate = build!.Index!.RowAt(indexRowId);
                indexRowId = build.Index.NextMatch(indexRowId);

                return true;
            }
        }
        else if(scanPosition < build!.Rows.Count)
        {
            candidate = build.Rows[scanPosition];
            scanPosition++;

            return true;
        }

        candidate = null!;

        return false;
    }

    /// <summary>Evaluates the optional condition on one merged row (resolving EXISTS through the pipeline's registry); a missing condition always satisfies.</summary>
    /// <param name="merged">The merged row.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the merge survives.</returns>
    private async ValueTask<bool> SatisfiesConditionAsync(SparqlSolution merged, CancellationToken cancellationToken)
    {
        if(condition is null)
        {
            return true;
        }

        ExpressionNode resolved = conditionHasExists
            ? await engine.ResolveExistsForPipelineAsync(condition, merged, activeGraph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false)
            : condition;

        return SparqlExpressionEvaluator.Satisfies(resolved, merged, engine.ExpressionContext);
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        //The drained build is binding-independent and retained; only the streamed outer side re-arms.
        await left.ResetAsync(preBinding).ConfigureAwait(false);
        currentLeft = null;
        current = null;
        indexRowId = -1;
        scanPosition = 0;
        anySatisfied = false;
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
