using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// Factory methods for planners shipped by the library.
/// </summary>
/// <remarks>
/// <para>
/// Each factory constructs a planner-bearing instance and returns
/// the matching <see cref="Planner"/> delegate via method-group
/// conversion. No closures are involved; the captured state lives
/// on the instance.
/// </para>
/// <para>
/// <b>Lifetime.</b> The instance is reachable only through the
/// returned delegate's target. Holding the delegate keeps the
/// instance alive; releasing the delegate makes the instance
/// eligible for collection. Consumers needing direct access to
/// the instance for diagnostics should construct the planner
/// type directly (for example <see cref="FirstOccurrencePlanner"/>)
/// rather than going through this factory.
/// </para>
/// </remarks>
public static class Planners
{
    /// <summary>
    /// Returns a planner that descends variables in their order
    /// of first occurrence in <paramref name="query"/>. See
    /// <see cref="FirstOccurrencePlanner"/> for the full
    /// decision rules.
    /// </summary>
    /// <param name="query">The basic graph pattern; must not be <c>null</c>.</param>
    /// <returns>A <see cref="Planner"/> delegate consulting a fresh planner instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <c>null</c>.</exception>
    public static Planner FirstOccurrence(BasicGraphPattern query)
    {
        ArgumentNullException.ThrowIfNull(query);

        FirstOccurrencePlanner planner = new(query);

        return planner.Plan;
    }

    /// <summary>
    /// Returns a planner that descends variables in exactly the
    /// supplied order — for engines whose iterator construction has
    /// already committed to one global elimination order (a
    /// rotation-constrained columnar index), where the dynamic
    /// choice space has collapsed to that single order. Decision
    /// rules otherwise match <see cref="FirstOccurrencePlanner"/>:
    /// any exhausted iterator skips the branch, all variables bound
    /// yields.
    /// </summary>
    /// <param name="order">The global variable elimination order; must cover every variable the query binds.</param>
    /// <returns>A <see cref="Planner"/> delegate consulting a fresh planner instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is <c>null</c>.</exception>
    public static Planner FixedOrder(IReadOnlyList<Variable> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        FixedOrderPlanner planner = new(order);

        return planner.Plan;
    }

    /// <summary>The instance behind <see cref="FixedOrder"/>: first-occurrence rules over a caller-fixed order.</summary>
    private sealed class FixedOrderPlanner
    {
        /// <summary>The fixed global elimination order.</summary>
        private IReadOnlyList<Variable> Order { get; }

        /// <summary>Constructs the planner over its fixed order.</summary>
        /// <param name="order">The global variable elimination order.</param>
        public FixedOrderPlanner(IReadOnlyList<Variable> order)
        {
            Order = order;
        }

        /// <summary>Returns the decision for the current state; method-group convertible to <see cref="Planner"/>.</summary>
        /// <param name="context">The current state of the descent.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Skip on any exhausted iterator, yield when every variable is bound, otherwise descend the order's next variable.</returns>
        public PlannerDecision Plan(PlannerContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for(int i = 0; i < context.Iterators.Count; i++)
            {
                if(context.Iterators[i].AtEnd)
                {
                    return PlannerDecision.SkipBranch();
                }
            }

            int boundCount = context.Bindings.Count;

            if(boundCount >= Order.Count)
            {
                return PlannerDecision.YieldSolution();
            }

            return PlannerDecision.DescendVariable(Order[boundCount]);
        }
    }
}
