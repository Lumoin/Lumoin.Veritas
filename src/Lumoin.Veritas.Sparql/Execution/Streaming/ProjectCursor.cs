using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>The streaming projection: restricts each pulled row to the projected variables, in projection order, dropping bindings for variables the row does not bind — the row form's semantics, one row at a time.</summary>
internal sealed class ProjectCursor : SolutionCursor
{
    private readonly SolutionCursor input;

    private readonly IReadOnlyList<SparqlVariable> variables;

    private SparqlSolution? current;

    /// <summary>Constructs the cursor over its input child.</summary>
    /// <param name="input">The input cursor.</param>
    /// <param name="variables">The projected variables, in projection order.</param>
    public ProjectCursor(SolutionCursor input, IReadOnlyList<SparqlVariable> variables)
    {
        this.input = input;
        this.variables = variables;
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
        List<SparqlBinding> bindings = new(variables.Count);
        foreach(SparqlVariable variable in variables)
        {
            if(solution.TryGetValue(variable, out RdfTerm value))
            {
                bindings.Add(new SparqlBinding(variable, value));
            }
        }

        current = new SparqlSolution(bindings);
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
        //The child disposes through the pipeline's flat-list teardown; this cursor holds no sources.
        current = null;

        return ValueTask.CompletedTask;
    }
}
