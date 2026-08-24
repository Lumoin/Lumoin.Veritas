using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One planned Free Join flat run from <see cref="FreeJoinPipeline.TryPlan"/>: the patterns the relations
/// scan, the global descent order they build against, the per-relation build plans, and the summary values
/// a plan-applied trace event reads. The summaries are computed once at plan time, each on the basis its
/// own name states.
/// </summary>
/// <param name="Patterns">The query's patterns, positional against <paramref name="Relations"/>.</param>
/// <param name="Order">The global descent order the executor binds variables in.</param>
/// <param name="Relations">The per-relation build plans, positional against <paramref name="Patterns"/>.</param>
/// <param name="RelationCount">The number of relations this run builds — the query's pattern count.</param>
/// <param name="FullDepthRelationCount">How many relations this run builds through their last column, measured on the APPLIED depths: a relation whose join cover already spans every column counts, and so does one the depth rule extended.</param>
/// <param name="PlannedTailBearingRelationCount">How many relations a join-cover build leaves a private tail on, measured on the COVER baseline before any extension, through <see cref="FreeJoinPipeline.TailBearingRelationCount"/>. An extension never moves it.</param>
/// <param name="FullDepthRelationMask">One bit per relation, set where the relation builds at full depth, indexed by plan position. Saturating: a pattern count has no cap, so relations at position sixty-four and beyond set no bit while all three counts stay exact at any width.</param>
internal sealed record FreeJoinPlan(
    IReadOnlyList<TriplePattern> Patterns,
    IReadOnlyList<Variable> Order,
    FreeJoinRelationPlan[] Relations,
    int RelationCount,
    int FullDepthRelationCount,
    int PlannedTailBearingRelationCount,
    long FullDepthRelationMask);
