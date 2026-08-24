using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One planned batched pipeline from
/// <see cref="ColumnarBatchPipeline.TryPlan"/>: the membership
/// constraints, the ordered scan patterns, the output schema, and —
/// when the matching policy is enabled and the shape qualifies — the
/// join tree Yannakakis' semijoin passes walk or the shared key that
/// routes a star through the factorising join.
/// </summary>
/// <param name="Constraints">The fully-bound patterns, checked as membership constraints before any scan.</param>
/// <param name="Order">The variable-bearing patterns in execution order: the first scans, the rest hash-join left-deep.</param>
/// <param name="Schema">The pipeline's output schema — every variable in accumulation order, byte-identical to the column order the executed pipeline produces.</param>
/// <param name="JoinTree">The join tree over <paramref name="Order"/> driving Yannakakis' semijoin reduction, or <see langword="null"/> when the pipeline runs the unreduced left-deep stream.</param>
/// <param name="StarKey">The variables every pattern of <paramref name="Order"/> joins on — present only when the shape is a factorisable star, routing the run through the factorising join so the intermediates stay product-of-unions until the final flatten; <see langword="null"/> otherwise.</param>
/// <param name="ChainNestVariable">The branch variable the third pattern joins on — present only when the shape is a three-pattern factorisable chain, routing the run through the join then the nesting <c>NestBranch</c> step so the chain stays factorised (a second level) across the branch-variable join; <see langword="null"/> otherwise.</param>
public sealed record ColumnarBatchPlan(
    IReadOnlyList<TriplePattern> Constraints,
    IReadOnlyList<TriplePattern> Order,
    IReadOnlyList<Variable> Schema,
    GyoJoinTree? JoinTree = null,
    IReadOnlyList<Variable>? StarKey = null,
    Variable? ChainNestVariable = null);
