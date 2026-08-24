namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// What the caller of one query wants that query's join to do, per axis. A hint is a preference, not a
/// force: it outranks the statistics on the axis it names — the caller knows its workload — and yields to
/// every policy force, because the operator outranks the caller. The engine composes the order, so
/// honouring a hint is not a selector's convention; a hinted route that declines the shape costs a
/// fall-through and never an answer, and an access-controlled query is never put to hints at all.
/// </summary>
/// <remarks>
/// The default-constructed value hints nothing on every axis, so passing none is the same as passing this.
/// </remarks>
/// <param name="Route">Which view-borne route to take; <see cref="JoinRouteHintKind.None"/> hints no route.</param>
/// <param name="Depth">Which depth the Free Join route's relations should build at; <see cref="FreeJoinDepthPolicy.Unspecified"/> hints no depth.</param>
/// <param name="Build">How the Free Join route's tries should materialise their maps; <see cref="FreeJoinTrieBuildPreference.Unspecified"/> hints no build mode.</param>
/// <param name="Factorization">Which factorising route the batched pipeline should engage; <see cref="FactorizationEngagement.Unspecified"/> hints no engagement.</param>
public readonly record struct JoinQueryHints(
    JoinRouteHintKind Route,
    FreeJoinDepthPolicy Depth,
    FreeJoinTrieBuildPreference Build,
    FactorizationEngagement Factorization);
