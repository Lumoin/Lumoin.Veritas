using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// What the <see cref="Planner"/> sees at one consultation
/// point during query execution.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot semantics.</b> Every collection in this context
/// is built fresh by the driver for the current consultation
/// and is valid only for that consultation. Holding references
/// across consultations produces stale data with no warning.
/// </para>
/// <para>
/// <b>Read-only contract.</b> The planner reads from this
/// context to choose its next action; it must not mutate
/// anything reachable through it. The driver passes
/// <see cref="IReadOnlyList{T}"/> rather than mutable lists to
/// make this contract explicit.
/// </para>
/// <para>
/// <b>What's here, and why.</b>
/// <list type="bullet">
/// <item><description><see cref="Query"/> — the basic graph pattern under evaluation. Stable for the query's lifetime; included so a stateless planner can reach it without closing over driver state.</description></item>
/// <item><description><see cref="Bindings"/> — the variables the iterators have bound so far, paired with the values they took. Empty at the first consultation, populated as the descent progresses.</description></item>
/// <item><description><see cref="Iterators"/> — a read-only snapshot of every iterator's current state. Selectivity-aware planners use this to pick the most-constraining next variable.</description></item>
/// <item><description><see cref="RecentDenials"/> — the triples access control has refused recently. Adaptive planners can re-cost on the basis of denial frequency; static planners ignore it.</description></item>
/// <item><description><see cref="Cardinalities"/> — a-priori per-class upper bounds derived from TBox classification, or <c>null</c> when the caller supplied none. Stable for the store generation; selectivity-aware planners cost class-membership patterns from it, static planners ignore it.</description></item>
/// </list>
/// </para>
/// </remarks>
public readonly record struct PlannerContext(
    BasicGraphPattern Query,
    IReadOnlyList<VariableBinding> Bindings,
    IReadOnlyList<IteratorSnapshot> Iterators,
    IReadOnlyList<EncodedTriple> RecentDenials,
    AprioriCardinalities? Cardinalities = null);
