using System.Collections.Immutable;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The result of diffing two named worlds on a mutable database: the outcome, and on
/// <see cref="WorldDiffOutcome.Diffed"/> the decoded per-graph transitions — the default graph first
/// and named graphs in ascending graph-id order, empty when the two worlds' states are identical.
/// </summary>
/// <param name="Outcome">The diff outcome.</param>
/// <param name="Transitions">The decoded transitions; meaningful only when <paramref name="Outcome"/> is <see cref="WorldDiffOutcome.Diffed"/>, empty otherwise.</param>
public readonly record struct WorldDiff(WorldDiffOutcome Outcome, ImmutableArray<WorldGraphTransition> Transitions);
