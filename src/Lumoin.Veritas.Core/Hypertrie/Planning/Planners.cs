using System;
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
}
