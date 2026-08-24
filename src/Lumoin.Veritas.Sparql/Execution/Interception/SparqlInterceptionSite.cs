using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Execution.Streaming;

namespace Lumoin.Veritas.Sparql.Execution.Interception;

/// <summary>
/// The evaluation state an interception entry consults at one expand-phase visit — the explicit frame the
/// driver binds per node: the engine (whose internal fast-path machinery the entries call), the active
/// graph, the evaluation's trace sink, and the streaming state the window entry threads into its transient
/// pipeline. Exactly the state the shipped fast paths consumed inline; nothing more rides here.
/// </summary>
internal readonly struct SparqlInterceptionSite
{
    /// <summary>Constructs the frame for one expand-phase visit.</summary>
    /// <param name="engine">The evaluating engine.</param>
    /// <param name="graph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="trace">The evaluation's trace sink.</param>
    /// <param name="rewrites">The evaluation's resolved rewrite pipeline, threaded into a transient pipeline's EXISTS plan builds.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's <c>EXISTS</c> re-entry depth.</param>
    public SparqlInterceptionSite(SparqlQueryEngine engine, TermId graph, SparqlExecutionTrace trace, AlgebraRewritePipeline rewrites, CursorBudget cursorBudget, int existsDepth)
    {
        Engine = engine;
        Graph = graph;
        Trace = trace;
        Rewrites = rewrites;
        CursorBudget = cursorBudget;
        ExistsDepth = existsDepth;
    }

    /// <summary>The evaluating engine.</summary>
    public SparqlQueryEngine Engine { get; }

    /// <summary>The active graph, or <see cref="TermId.None"/> for the default graph.</summary>
    public TermId Graph { get; }

    /// <summary>The evaluation's trace sink.</summary>
    public SparqlExecutionTrace Trace { get; }

    /// <summary>The evaluation's resolved rewrite pipeline, threaded into a transient pipeline's EXISTS plan builds.</summary>
    public AlgebraRewritePipeline Rewrites { get; }

    /// <summary>The evaluation's shared cursor-budget cell.</summary>
    public CursorBudget CursorBudget { get; }

    /// <summary>The evaluation's <c>EXISTS</c> re-entry depth.</summary>
    public int ExistsDepth { get; }
}
