namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One registered world in a <see cref="DatasetWorlds"/> snapshot: the world's name, the name of the
/// world it was forked from, and the world itself. The parent name is fork lineage, not a live
/// reference — it names the source world at fork time and stands even after that world's name is
/// dropped from the registry, so a lineage reader treats it as history rather than as a lookup key.
/// </summary>
/// <param name="Name">The world's registered name.</param>
/// <param name="Parent">The name of the world this one was forked from, or <see langword="null"/> for the registry's seed world.</param>
/// <param name="World">The registered world.</param>
public readonly record struct DatasetWorldEntry(string Name, string? Parent, MutableSparqlDataset World);
