using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One planned batched pipeline from
/// <see cref="ColumnarBatchPipeline.TryPlan"/>: the membership
/// constraints, the ordered scan patterns, the output schema, and —
/// when semijoin reduction is enabled and the shape qualifies — the
/// join tree the reducing passes walk.
/// </summary>
/// <param name="Constraints">The fully-bound patterns, checked as membership constraints before any scan.</param>
/// <param name="Order">The variable-bearing patterns in execution order: the first scans, the rest hash-join left-deep.</param>
/// <param name="Schema">The pipeline's output schema — every variable in accumulation order, byte-identical to the column order the executed pipeline produces.</param>
/// <param name="JoinTree">The join tree over <paramref name="Order"/> driving Yannakakis' semijoin reduction, or <see langword="null"/> when the pipeline runs the unreduced left-deep stream.</param>
public sealed record ColumnarBatchPlan(
    IReadOnlyList<TriplePattern> Constraints,
    IReadOnlyList<TriplePattern> Order,
    IReadOnlyList<Variable> Schema,
    GyoJoinTree? JoinTree = null);
