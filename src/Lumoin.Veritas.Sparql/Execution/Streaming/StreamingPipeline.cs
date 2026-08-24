using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Algebra;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// A compiled pull pipeline over an algebra plan: the root cursor plus the flat cursor list teardown and trace
/// emission walk (construction order, children before parents — an explicit worklist, never tree recursion).
/// Compilation opens no sources — every cursor opens lazily on its first pull — so an abandoned compile holds
/// nothing and a partially-pulled pipeline is torn down exactly once by <see cref="DisposeAsync"/>. Consumers
/// pull inside try/finally; nothing auto-disposes on a throwing or cancelled pull.
/// </summary>
internal sealed class StreamingPipeline : System.IAsyncDisposable
{
    /// <summary>
    /// The per-evaluation CUMULATIVE cursor budget: the maximum live cursor pull frames one evaluation may
    /// stack across all its pipelines, materialise-boundary re-entries, and driver interceptions. Pull nesting
    /// follows the algebra's height, which is attacker-controllable, and every level is a
    /// <see cref="System.Threading.Tasks.ValueTask{TResult}"/> async frame — so compilation charges this budget
    /// per cursor and a subtree that would exceed it stays on the stack-safe materialising path. Fresh budgets
    /// exist only at the public evaluation entries; every re-entry channel threads the remaining budget.
    /// </summary>
    internal const int MaxCursorDepth = 64;

    private readonly List<SolutionCursor> cursors;

    private readonly ExistsRegistry? existsRegistry;

    private readonly List<(int Cursor, SparqlExecutionOperator Operator, int Left, int Right)>? traceEntries;

    private readonly SparqlExecutionTrace? completionTrace;

    private bool disposed;

    /// <summary>Wraps the compiled cursor tree; called by the compile factories only.</summary>
    /// <param name="root">The root cursor the consumer pulls.</param>
    /// <param name="cursors">The flat cursor list in construction order (children before parents).</param>
    /// <param name="existsRegistry">The pipeline-owned EXISTS plan registry the expression cursors compile through, or <see langword="null"/> when the plan carries no expression cursor.</param>
    /// <param name="traceEntries">The traced cursors with their operator kinds and child indices, or <see langword="null"/> when emission is suppressed.</param>
    /// <param name="completionTrace">The spawning evaluation's trace sink the completion walk emits into, or <see langword="null"/> when suppressed (per-binding EXISTS pipelines and untraced evaluations).</param>
    private StreamingPipeline(SolutionCursor root, List<SolutionCursor> cursors, ExistsRegistry? existsRegistry, List<(int Cursor, SparqlExecutionOperator Operator, int Left, int Right)>? traceEntries, SparqlExecutionTrace? completionTrace)
    {
        Root = root;
        this.cursors = cursors;
        this.existsRegistry = existsRegistry;
        this.traceEntries = traceEntries;
        this.completionTrace = completionTrace;
    }

    /// <summary>The root cursor the consumer pulls.</summary>
    public SolutionCursor Root { get; }

    /// <summary>The number of cursors this pipeline holds — the budget units its compilation consumed.</summary>
    public int CursorCount => cursors.Count;

    /// <summary>The flat cursor list in construction order (children before parents) — the teardown and trace walk, and the bounded-work pins' per-cursor observables.</summary>
    public IReadOnlyList<SolutionCursor> Cursors => cursors;

    /// <summary>
    /// Compiles an algebra plan into a cursor pipeline under the active graph, charging
    /// <paramref name="budget"/> one unit per constructed cursor (a declined compile refunds its charges —
    /// nothing lives; a compiled pipeline stays charged for the evaluation's lifetime). A streamable leaf
    /// becomes its cursor; every other operator becomes a lazy <see cref="MaterializedCursor"/> boundary
    /// evaluated by the materialising executor on first pull — the pipeline is therefore TOTAL: worst case
    /// the whole plan is one boundary and the streaming mode degenerates to the incumbent behaviour.
    /// Declines (returns <see langword="null"/>) when the budget cannot afford the compilation; a declined
    /// or abandoned compile needs no teardown because construction opens no sources.
    /// </summary>
    /// <param name="engine">The engine whose materialising driver evaluates boundary subtrees.</param>
    /// <param name="machinery">The shared BGP machinery the leaf cursors pull through.</param>
    /// <param name="root">The algebra plan to compile.</param>
    /// <param name="activeGraph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="budget">The evaluation's shared cursor-budget cell; public entries create it with <see cref="MaxCursorDepth"/>.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at this position; boundary cursors carry it into their first-pull driver re-entry.</param>
    /// <param name="rewrites">The evaluation's resolved rewrite pipeline, carried on the pipeline-owned EXISTS registry so in-pipeline sites compile rewritten inner algebra.</param>
    /// <param name="completionTrace">The spawning evaluation's trace sink the completion walk emits into, or <see langword="null"/> when suppressed.</param>
    /// <returns>The compiled pipeline, or <see langword="null"/> when the budget declines it.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the registry and every constructed cursor transfers to the returned StreamingPipeline, whose DisposeAsync tears them down; a declined compile drops them before any source or plan (and thus any owned resource) can exist — construction opens nothing.")]
    public static StreamingPipeline? TryCompile(SparqlQueryEngine engine, BgpMachinery machinery, AlgebraOperator root, TermId activeGraph, CursorBudget budget, int existsDepth, Algebra.Rewriting.AlgebraRewritePipeline rewrites, SparqlExecutionTrace? completionTrace = null)
    {
        //Two-phase expand/combine over an explicit stack (the driver's own idiom): a streamable composite
        //pushes its children before combining; a leaf or a non-streamable subtree constructs immediately (a
        //boundary never descends — its operators evaluate through the incumbent driver on first pull). Each
        //Slice records the flat-list position its input subtree starts at, so the order gate can roll a
        //reordering window's subtree back (refunding its charges — nothing opened) onto the boundary path.
        List<SolutionCursor> cursorsToAppendTo = [];
        List<(int Cursor, SparqlExecutionOperator Operator, int Left, int Right)> traceEntriesToAppendTo = [];
        Dictionary<AlgebraOperator, SolutionCursor> built = new(ReferenceEqualityComparer.Instance);
        Dictionary<AlgebraOperator, int> builtIndex = new(ReferenceEqualityComparer.Instance);
        ExistsRegistry registry = new(rewrites, completionTrace);
        Stack<(AlgebraOperator Node, bool Combine, int SubtreeStart)> work = new();
        work.Push((root, Combine: false, cursorsToAppendTo.Count));

        while(work.Count > 0)
        {
            (AlgebraOperator node, bool combine, int subtreeStart) = work.Pop();
            if(!combine)
            {
                if(IsStreamableComposite(node))
                {
                    work.Push((node, Combine: true, cursorsToAppendTo.Count));
                    IReadOnlyList<AlgebraOperator> children = node.Children;
                    for(int i = children.Count - 1; i >= 0; i--)
                    {
                        work.Push((children[i], Combine: false, cursorsToAppendTo.Count));
                    }

                    continue;
                }

                if(TryConstructLeafOrBoundary(engine, machinery, node, activeGraph, budget, existsDepth, completionTrace, cursorsToAppendTo) is not SolutionCursor constructed)
                {
                    budget.Remaining += cursorsToAppendTo.Count;

                    return null;
                }

                built[node] = constructed;
                builtIndex[node] = cursorsToAppendTo.Count - 1;
                if(node is Bgp)
                {
                    traceEntriesToAppendTo.Add((cursorsToAppendTo.Count - 1, SparqlExecutionOperator.Bgp, -1, -1));
                }

                continue;
            }

            //The order-sensitivity gate: a position-based window engages the streaming path only when
            //every cursor strictly between it and the frontier is order-preserving; otherwise the WHOLE
            //Slice subtree rolls back onto the materialise boundary (its cursors opened nothing — refund).
            if(node is Slice slice && AnyReordering(cursorsToAppendTo, subtreeStart))
            {
                budget.Remaining += cursorsToAppendTo.Count - subtreeStart;
                cursorsToAppendTo.RemoveRange(subtreeStart, cursorsToAppendTo.Count - subtreeStart);
                traceEntriesToAppendTo.RemoveAll(TraceEntryPruner.For(subtreeStart).IsInRolledBackRange);
                if(TryConstructLeafOrBoundary(engine, machinery, slice, activeGraph, budget, existsDepth, completionTrace, cursorsToAppendTo) is not SolutionCursor boundary)
                {
                    budget.Remaining += cursorsToAppendTo.Count;

                    return null;
                }

                built[node] = boundary;
                builtIndex[node] = cursorsToAppendTo.Count - 1;

                continue;
            }

            if(TryConstructComposite(engine, node, activeGraph, budget, existsDepth, registry, built, cursorsToAppendTo) is not SolutionCursor composite)
            {
                budget.Remaining += cursorsToAppendTo.Count;

                return null;
            }

            built[node] = composite;
            builtIndex[node] = cursorsToAppendTo.Count - 1;
            RecordCompositeTrace(node, builtIndex, cursorsToAppendTo.Count - 1, traceEntriesToAppendTo);
        }

        return new StreamingPipeline(built[root], cursorsToAppendTo, registry, completionTrace is null ? null : traceEntriesToAppendTo, completionTrace);
    }

    /// <summary>Records a composite cursor's trace entry (operator kind + child indices for the completion walk's RowsLeft/Right); the structural pass-throughs carry no strategy decision and are not traced, matching the materialised path.</summary>
    /// <param name="node">The combined operator.</param>
    /// <param name="builtIndex">The flat-list index per constructed operator.</param>
    /// <param name="cursorIndex">The composite's own flat-list index.</param>
    /// <param name="traceEntriesToAppendTo">The accumulating trace entries.</param>
    private static void RecordCompositeTrace(AlgebraOperator node, Dictionary<AlgebraOperator, int> builtIndex, int cursorIndex, List<(int Cursor, SparqlExecutionOperator Operator, int Left, int Right)> traceEntriesToAppendTo)
    {
        switch(node)
        {
            case Project project: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Project, builtIndex[project.Input], -1)); break;

            case Slice slice: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Slice, builtIndex[slice.Input], -1)); break;

            case Filter filter: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Filter, builtIndex[filter.Input], -1)); break;

            case Extend extend: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Extend, builtIndex[extend.Input], -1)); break;

            case Distinct distinct: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Distinct, builtIndex[distinct.Input], -1)); break;

            case Union union: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Union, builtIndex[union.Left], builtIndex[union.Right])); break;

            case Join join: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Join, builtIndex[join.Left], builtIndex[join.Right])); break;

            case LeftJoin leftJoin: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.LeftJoin, builtIndex[leftJoin.Left], builtIndex[leftJoin.Right])); break;

            case Minus minus: traceEntriesToAppendTo.Add((cursorIndex, SparqlExecutionOperator.Minus, builtIndex[minus.Left], builtIndex[minus.Right])); break;

            default: break;
        }
    }

    /// <summary>The subtree rollback's trace-entry pruning predicate, carried as explicit state so the removal is a bound method group rather than a lambda closing over the range start.</summary>
    /// <param name="subtreeStart">The rolled-back range's first flat-list position.</param>
    private readonly struct TraceEntryPruner(int subtreeStart)
    {
        /// <summary>Builds the pruner for a rolled-back range.</summary>
        /// <param name="subtreeStart">The range's first flat-list position.</param>
        /// <returns>The pruner.</returns>
        public static TraceEntryPruner For(int subtreeStart) => new(subtreeStart);

        /// <summary>Whether a trace entry points into the rolled-back range.</summary>
        /// <param name="entry">The trace entry.</param>
        /// <returns><see langword="true"/> when the entry's cursor was removed.</returns>
        public bool IsInRolledBackRange((int Cursor, SparqlExecutionOperator Operator, int Left, int Right) entry) => entry.Cursor >= subtreeStart;
    }

    /// <summary>Whether the operator compiles to a composite cursor over its children (descended), as opposed to a leaf or a materialise boundary (never descended).</summary>
    /// <param name="node">The operator.</param>
    /// <returns><see langword="true"/> for the streamable composite set.</returns>
    private static bool IsStreamableComposite(AlgebraOperator node)
    {
        return node is Project or Slice or Filter or Extend or Union or Reduced or ToList or ToMultiSet or Distinct or Join or LeftJoin or Minus;
    }

    /// <summary>Whether any cursor from <paramref name="subtreeStart"/> onward reorders its input (the order gate's scan over the window's subtree — cursors below a boundary do not exist, so the flat range is exactly "strictly between the window and the frontier").</summary>
    /// <param name="cursors">The flat construction-order list.</param>
    /// <param name="subtreeStart">The subtree's first cursor position.</param>
    /// <returns><see langword="true"/> when a reordering cursor is present.</returns>
    private static bool AnyReordering(List<SolutionCursor> cursors, int subtreeStart)
    {
        for(int i = subtreeStart; i < cursors.Count; i++)
        {
            if(!cursors[i].IsOrderPreserving)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Constructs a streamable composite cursor over its already-built children, charging the budget one unit.</summary>
    /// <param name="engine">The engine whose expression machinery the expression cursors evaluate through.</param>
    /// <param name="node">The composite operator.</param>
    /// <param name="activeGraph">The active graph.</param>
    /// <param name="budget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at this position.</param>
    /// <param name="registry">The pipeline-owned EXISTS plan registry.</param>
    /// <param name="built">The children already constructed, by operator reference.</param>
    /// <param name="cursorsToAppendTo">The pipeline's flat cursor list.</param>
    /// <returns>The composite cursor, or <see langword="null"/> when the budget cannot afford it.</returns>
    private static SolutionCursor? TryConstructComposite(SparqlQueryEngine engine, AlgebraOperator node, TermId activeGraph, CursorBudget budget, int existsDepth, ExistsRegistry registry, Dictionary<AlgebraOperator, SolutionCursor> built, List<SolutionCursor> cursorsToAppendTo)
    {
        if(budget.Remaining < 1)
        {
            return null;
        }

        budget.Remaining--;
        SolutionCursor cursor = node switch
        {
            Project project => new ProjectCursor(built[project.Input], project.Variables),
            Slice slice => new SliceCursor(built[slice.Input], slice.Offset, slice.Limit),
            Filter filter => new FilterCursor(engine, built[filter.Input], filter.Condition, activeGraph, registry, budget, existsDepth),
            Extend extend => new ExtendCursor(engine, built[extend.Input], extend.Variable, extend.Expression, activeGraph, registry, budget, existsDepth),
            Union union => new UnionCursor(built[union.Left], built[union.Right]),
            Reduced reduced => new PassThroughCursor(built[reduced.Input]),
            ToList toList => new PassThroughCursor(built[toList.Input]),
            ToMultiSet toMultiSet => new PassThroughCursor(built[toMultiSet.Input]),
            Distinct distinct => new DistinctCursor(built[distinct.Input]),
            Join join => new JoinCursor(built[join.Left], built[join.Right]),
            LeftJoin leftJoin => new LeftJoinCursor(engine, built[leftJoin.Left], built[leftJoin.Right], leftJoin.Condition, activeGraph, registry, budget, existsDepth),
            Minus minus => new MinusCursor(built[minus.Left], built[minus.Right]),
            _ => throw new System.InvalidOperationException($"Operator '{node.GetType().Name}' is not a streamable composite."),
        };

        cursorsToAppendTo.Add(cursor);

        return cursor;
    }

    /// <summary>
    /// Compiles the seeded <c>EXISTS</c> probe pipeline: a single <see cref="BgpCursor"/> over the seed
    /// plan's encoding skeleton, re-armed per binding by patching the bound seed positions. Declines when
    /// the budget cannot afford the one cursor.
    /// </summary>
    /// <param name="machinery">The shared BGP machinery.</param>
    /// <param name="seedPlan">The site's seeding plan.</param>
    /// <param name="activeGraph">The active graph the probe queries.</param>
    /// <param name="budget">The evaluation's shared cursor-budget cell.</param>
    /// <returns>The compiled probe pipeline, or <see langword="null"/> when the budget declines it.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the cursor transfers to the returned StreamingPipeline, whose DisposeAsync tears it down; construction opens no sources.")]
    public static StreamingPipeline? TryCompileSeededExists(BgpMachinery machinery, BgpSeedPlan seedPlan, TermId activeGraph, CursorBudget budget)
    {
        if(budget.Remaining < 1)
        {
            return null;
        }

        budget.Remaining--;
        BgpCursor cursor = new(machinery, seedPlan.Skeleton, activeGraph, seedPlan);

        //Per-binding EXISTS pipelines emit no per-cursor trace events.
        return new StreamingPipeline(cursor, [cursor], existsRegistry: null, traceEntries: null, completionTrace: null);
    }

    /// <summary>
    /// Constructs a leaf (<see cref="Bgp"/>, inline <see cref="Table"/>, <see cref="UnitTable"/>) or a lazy
    /// materialise boundary over a whole non-streamable subtree (no descent past a boundary — the enclosed
    /// operators evaluate through the incumbent driver, which the boundary re-enters with the SAME budget
    /// cell and EXISTS depth, so nested pipelines draw from this evaluation's remaining budget).
    /// </summary>
    /// <param name="engine">The engine whose materialising driver evaluates boundary subtrees.</param>
    /// <param name="machinery">The shared BGP machinery.</param>
    /// <param name="node">The node to construct for.</param>
    /// <param name="activeGraph">The active graph.</param>
    /// <param name="budget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at this position.</param>
    /// <param name="completionTrace">The spawning evaluation's trace sink a boundary's first-pull re-entry emits into, or <see langword="null"/> when the pipeline compiles without one.</param>
    /// <param name="cursorsToAppendTo">The pipeline's flat cursor list; every constructed cursor is appended in construction order.</param>
    /// <returns>The node's cursor, or <see langword="null"/> when the budget cannot afford it.</returns>
    private static SolutionCursor? TryConstructLeafOrBoundary(SparqlQueryEngine engine, BgpMachinery machinery, AlgebraOperator node, TermId activeGraph, CursorBudget budget, int existsDepth, SparqlExecutionTrace? completionTrace, List<SolutionCursor> cursorsToAppendTo)
    {
        if(budget.Remaining < 1)
        {
            return null;
        }

        budget.Remaining--;
        SolutionCursor cursor = node switch
        {
            Bgp bgp => new BgpCursor(machinery, bgp, activeGraph),
            Table table => new TableCursor(BgpMachinery.BuildTableSolutions(table.Data)),
            UnitTable => new UnitCursor(),
            _ => new MaterializedCursor(engine, node, activeGraph, budget, existsDepth, completionTrace),
        };

        cursorsToAppendTo.Add(cursor);

        return cursor;
    }

    /// <summary>
    /// Tears the pipeline down exactly once: FIRST the completion-walk trace emission (drain or abandon —
    /// each traced cursor's event carries the rows it ACTUALLY produced, in reverse construction order over
    /// the flat list; an explicit worklist, no tree recursion), then the cursors in reverse construction
    /// order (parents before children; safe on a partially-advanced chain), then the pipeline-owned EXISTS
    /// registry (its plans' pools and nested probe pipelines).
    /// </summary>
    /// <returns>A task completing when every cursor and the registry are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        if(disposed)
        {
            return;
        }

        disposed = true;
        if(completionTrace is { IsEnabled: true } trace && traceEntries is not null)
        {
            for(int i = traceEntries.Count - 1; i >= 0; i--)
            {
                (int cursorIndex, SparqlExecutionOperator @operator, int left, int right) = traceEntries[i];
                trace.EmitStreaming(
                    @operator,
                    left >= 0 ? cursors[left].RowsProduced : -1,
                    right >= 0 ? cursors[right].RowsProduced : -1,
                    cursors[cursorIndex].RowsProduced);
            }
        }

        for(int i = cursors.Count - 1; i >= 0; i--)
        {
            await cursors[i].DisposeAsync().ConfigureAwait(false);
        }

        if(existsRegistry is ExistsRegistry registry)
        {
            await registry.DisposeAsync().ConfigureAwait(false);
        }
    }
}
