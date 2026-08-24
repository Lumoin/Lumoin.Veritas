using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming <c>BIND</c>/<c>Extend</c>: per pulled row, evaluates the expression (resolving any
/// <c>EXISTS</c> through the owning pipeline's plan registry) and appends the binding — the materialising
/// row path's semantics exactly: an erring expression keeps the row with the variable unbound, and the
/// extended row is linked into the per-row blank-node scope so <c>BNODE</c> correlation is preserved.
/// </summary>
internal sealed class ExtendCursor : SolutionCursor
{
    private readonly SparqlQueryEngine engine;

    private readonly SolutionCursor input;

    private readonly SparqlVariable variable;

    private readonly ExpressionNode expression;

    private readonly bool expressionHasExists;

    private readonly TermId activeGraph;

    private readonly ExistsRegistry existsRegistry;

    private readonly CursorBudget cursorBudget;

    private readonly int existsDepth;

    private SparqlSolution? current;

    /// <summary>Constructs the cursor over its input child.</summary>
    /// <param name="engine">The engine whose expression machinery evaluates the bind.</param>
    /// <param name="input">The input cursor.</param>
    /// <param name="variable">The bound variable.</param>
    /// <param name="expression">The bind expression.</param>
    /// <param name="activeGraph">The active graph any EXISTS in the expression re-enters in.</param>
    /// <param name="existsRegistry">The owning pipeline's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at the pipeline's position.</param>
    public ExtendCursor(SparqlQueryEngine engine, SolutionCursor input, SparqlVariable variable, ExpressionNode expression, TermId activeGraph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth)
    {
        this.engine = engine;
        this.input = input;
        this.variable = variable;
        this.expression = expression;
        expressionHasExists = SparqlQueryEngine.ContainsExists(expression);
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
        if(!await input.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        SparqlSolution solution = input.Current;
        ExpressionNode resolved = expressionHasExists
            ? await engine.ResolveExistsForPipelineAsync(expression, solution, activeGraph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false)
            : expression;

        if(!SparqlExpressionEvaluator.TryEvaluate(resolved, solution, engine.ExpressionContext, out RdfTerm value))
        {
            current = solution;
            RowsProduced++;

            return true;
        }

        List<SparqlBinding> bindings = new(solution.Bindings.Count + 1);
        bindings.AddRange(solution.Bindings);
        bindings.Add(new SparqlBinding(variable, value));
        SparqlSolution extended = new(bindings);

        //The extended row is a new object but the same solution for BNODE correlation: keep its per-row
        //blank-node scope so BNODE(key) before and after the extend agrees.
        engine.ExpressionContext.BlankNodeScope.Link(solution, extended);
        current = extended;
        RowsProduced++;

        return true;
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        await input.ResetAsync(preBinding).ConfigureAwait(false);
        current = null;
        RowsProduced = 0;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        current = null;

        return ValueTask.CompletedTask;
    }
}
