using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// What a <see cref="JoinStrategySelectorDelegate"/> sees at its one consultation per query: the pattern
/// under evaluation, the columnar view it would run on, and the shape features the engine measured for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Consultation-scoped.</b> The context is built fresh for one consultation and is valid only for that
/// call. <see cref="View"/> is the live serving view; a selector must not retain it past the call, and it
/// must not mutate anything reachable through this context.
/// </para>
/// <para>
/// <b>Why the query and the view are here.</b> A selector that wants a statistic the engine does not
/// pre-compute reads it from these two rather than closing over driver state — the same reason
/// <see cref="Lumoin.Veritas.Core.Hypertrie.Planning.PlannerContext"/> carries its query. Adding a
/// pre-computed statistic later is a property added to <see cref="JoinSelectionFeatures"/>, which no
/// existing selector has to acknowledge.
/// </para>
/// </remarks>
/// <param name="Query">The basic graph pattern under evaluation.</param>
/// <param name="View">The columnar view the chosen route would run on; borrowed for the consultation only.</param>
/// <param name="Features">The shape features the engine measured for this query on this view.</param>
/// <param name="Hints">What the caller asked of this one query, so a selector sees the hints for the axes they leave unspecified. The engine applies them itself, per axis and in its own order, so a selector never has to.</param>
public readonly record struct JoinSelectionContext(
    BasicGraphPattern Query,
    ColumnarTripleIndex View,
    JoinSelectionFeatures Features,
    JoinQueryHints Hints);
