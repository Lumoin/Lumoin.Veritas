using System;
using System.Diagnostics;
using System.Threading;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// The default static planner. Visits the query's variables in
/// the order they first occur in the basic graph pattern —
/// walking patterns left-to-right and within each pattern in
/// subject-predicate-object order. The order is computed once at
/// construction (it is what
/// <see cref="BasicGraphPattern.Variables"/> already holds) and
/// never changes during query execution.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decision rules.</b>
/// <list type="number">
/// <item><description>If any iterator in the context is at end, return <see cref="PlannerDecision.SkipBranch"/>. There is no way to extend the current bindings into a solution from this state.</description></item>
/// <item><description>If every variable in the query is already bound, return <see cref="PlannerDecision.YieldSolution"/>. Solution complete.</description></item>
/// <item><description>Otherwise, return <see cref="PlannerDecision.DescendVariable"/> for the next variable in the query's first-occurrence order — specifically <c>Query.Variables[Bindings.Count]</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Order stability.</b> Bindings are populated in the same
/// order this planner returns them; <c>Bindings.Count</c> is
/// therefore exactly the index of the next variable to descend
/// by. An adaptive replacement that hands off to this planner
/// mid-query must populate bindings in
/// <see cref="BasicGraphPattern.Variables"/> order too — or
/// substitute its own ordering and stop using this planner.
/// </para>
/// <para>
/// <b>No closures.</b> The planner is a class holding the basic
/// graph pattern as a property; <see cref="Plan"/> is consumed
/// via method-group conversion to the
/// <see cref="Planner"/> delegate. This avoids capturing the
/// query in a lambda — consistent with the project's
/// no-closure convention — and lets test code keep a reference
/// to the planner instance for assertions on its state.
/// </para>
/// <para>
/// <b>Thread safety.</b> The planner holds no mutable state.
/// One instance can be consulted from any number of threads;
/// each call reads <see cref="PlannerContext"/> and returns a
/// fresh <see cref="PlannerDecision"/>. The basic graph pattern
/// itself is treated as effectively read-only — see the
/// caveat on <see cref="BasicGraphPattern.Registry"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("FirstOccurrencePlanner Variables={Query.Variables.Count}")]
public sealed class FirstOccurrencePlanner
{
    /// <summary>The basic graph pattern this planner walks.</summary>
    public BasicGraphPattern Query { get; }

    /// <summary>
    /// Constructs a new planner over <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The basic graph pattern; must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <c>null</c>.</exception>
    public FirstOccurrencePlanner(BasicGraphPattern query)
    {
        ArgumentNullException.ThrowIfNull(query);

        Query = query;
    }

    /// <summary>
    /// Returns the planner's decision for the current state.
    /// Method-group convertible to the
    /// <see cref="Planner"/> delegate.
    /// </summary>
    /// <param name="context">The current state of the descent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decision per the rules in the type's remarks.</returns>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public PlannerDecision Plan(PlannerContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        //Rule 1: any iterator at end → skip branch.
        for(int i = 0; i < context.Iterators.Count; i++)
        {
            if(context.Iterators[i].AtEnd)
            {
                return PlannerDecision.SkipBranch();
            }
        }

        //Rule 2: every variable bound → yield.
        int boundCount = context.Bindings.Count;

        if(boundCount >= Query.Variables.Count)
        {
            return PlannerDecision.YieldSolution();
        }

        //Rule 3: descend by the next first-occurrence variable.
        Variable next = Query.Variables[boundCount];

        return PlannerDecision.DescendVariable(next);
    }
}
