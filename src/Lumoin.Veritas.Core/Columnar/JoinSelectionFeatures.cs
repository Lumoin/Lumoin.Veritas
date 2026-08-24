namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The shape features one join-route decision was taken on: the scalar half of
/// <see cref="JoinSelectionContext"/>, carried on the
/// <see cref="Lumoin.Veritas.Core.Hypertrie.Tracing.QueryTraceEventKind.EngineSelected"/> event so a
/// consumer joining it with <see cref="Lumoin.Veritas.Core.Hypertrie.Tracing.QueryTraceEventKind.QueryCompleted"/>
/// obtains (features, decision) to observed cost — the pairing an adaptive policy learns from.
/// </summary>
/// <remarks>
/// Every member is a value: this record holds no reference to a query, a view, or a store, so a consumer
/// that buffers trace events retains none of them. That is why the trace carries this record and never
/// <see cref="JoinSelectionContext"/>.
/// </remarks>
/// <param name="PatternCount">The query's pattern count.</param>
/// <param name="ViewTripleCount">The view's triple count.</param>
/// <param name="Acyclic">Whether the GYO reduction proves the variable-bearing patterns' hypergraph acyclic.</param>
/// <param name="ComponentCount">The number of connected components the variable-bearing patterns form over shared variables; two or more is a cartesian shape. Fully bound patterns bind no variable and form no component.</param>
/// <param name="OrderSetMode">The permutation set the view materialises.</param>
/// <param name="BatchedRouteEligible">Whether the batched scan-and-hash route is enabled by policy for this query. Informational: it names which routes exist, and never vetoes a decision.</param>
/// <param name="MaximumKeyFanOut">The largest matches one join-key value carries across the query's patterns, over every pattern and join variable whose statistic the view exposes: the data half of the skew signal. <see cref="UnreadableKeyFanOut"/> when the view exposes none for this query.</param>
/// <param name="TailBearingRelationCount">How many of the query's relations a join-cover build leaves a private tail on — the columns past the relation's last join variable in the route's global descent order. The structural half of the skew signal: fan-out concentrated on a key that two or more relations then multiply is what the generic join pays for, while a lone tail costs no more than its own matches. Zero on a shape whose every variable is a join variable (a cyclic core, where cover depth is already full depth), and <see cref="UnplannedTailBearingRelationCount"/> when the shape has no global descent order at all.</param>
/// <param name="DegreeWeightedMeanFanOut">The heaviest degree-weighted mean matches per join-key value across the query's patterns, over every pattern and join variable whose statistic the view exposes: the shape half of the skew signal, where <see cref="MaximumKeyFanOut"/> is its peak. A hub beside a long flat tail reads a high maximum and a low weighted mean, which is the pair a single concentrated key and a genuinely skewed distribution differ on. <see cref="UnreadableWeightedFanOut"/> when the view exposes none for this query.</param>
public readonly record struct JoinSelectionFeatures(
    int PatternCount,
    int ViewTripleCount,
    bool Acyclic,
    int ComponentCount,
    ColumnarOrderSetMode OrderSetMode,
    bool BatchedRouteEligible,
    int MaximumKeyFanOut,
    int TailBearingRelationCount,
    double DegreeWeightedMeanFanOut)
{
    /// <summary>
    /// The <see cref="MaximumKeyFanOut"/> of a query no pattern of which exposes the statistic — a fan-out
    /// of zero is a real reading (a key group that matches nothing), so the unreadable case carries its own
    /// value instead of borrowing that one.
    /// </summary>
    public const int UnreadableKeyFanOut = -1;

    /// <summary>
    /// The <see cref="TailBearingRelationCount"/> of a query the view materialises no global descent order
    /// for — a count of zero is a real reading (every variable is a join variable, so no relation carries a
    /// tail), so the unplanned case carries its own value instead of borrowing that one.
    /// </summary>
    public const int UnplannedTailBearingRelationCount = -1;

    /// <summary>
    /// The <see cref="DegreeWeightedMeanFanOut"/> of a query no pattern of which exposes the statistic. A
    /// weighted mean of zero is a real reading (a key group that matches nothing), so the unreadable case
    /// carries its own value; the statistic is a ratio and stays a ratio, since truncating it to an integer
    /// would alias distinct degree sequences onto one reading.
    /// </summary>
    public const double UnreadableWeightedFanOut = -1.0;
}
