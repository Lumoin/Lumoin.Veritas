using System.Collections.Immutable;

namespace Lumoin.Veritas.Core.Causality;

/// <summary>
/// One axis of a <see cref="CausalContext"/> coverage snapshot: the contiguous prefix maximum and the sorted
/// counters observed beyond it. A context whose every axis has an empty cloud is per-axis contiguous — a plain
/// version vector — which is the standing shape of every context this library's protocol produces: local mints
/// extend the own axis contiguously, and every reconcile fold joins whole contexts.
/// </summary>
/// <param name="Axis">The replica identity axis the coverage describes.</param>
/// <param name="PrefixMax">The largest counter N such that every counter in [1, N] on the axis is covered; 0 when none are.</param>
/// <param name="Cloud">The counters observed beyond the contiguous prefix, sorted ascending; empty for contiguous coverage.</param>
public readonly record struct CausalAxisCoverage(ReplicaAxis Axis, ulong PrefixMax, ImmutableArray<ulong> Cloud);
