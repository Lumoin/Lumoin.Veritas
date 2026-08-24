namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The result of a <see cref="DatasetWorlds.TryForkAsync"/> call: the outcome, and on
/// <see cref="WorldForkOutcome.Forked"/> the registered fork itself.
/// </summary>
/// <param name="Outcome">The fork outcome.</param>
/// <param name="World">The forked world, registered under the requested name; <see langword="null"/> unless <paramref name="Outcome"/> is <see cref="WorldForkOutcome.Forked"/>.</param>
public readonly record struct WorldFork(WorldForkOutcome Outcome, MutableSparqlDataset? World);
