namespace Lumoin.Veritas.Database;

/// <summary>
/// The outcome of dropping a named world on a mutable database. An unknown name is an expected
/// condition — surfaces race drops — so it answers as a value rather than an exception.
/// </summary>
public enum WorldDropOutcome
{
    /// <summary>The world's name was removed from the registry. Existing holders keep the world usable; once unreferenced, its unshared roots become sweepable through the arena's weak registries.</summary>
    Dropped,

    /// <summary>No world is registered under the requested name; nothing changed.</summary>
    UnknownWorld,

    /// <summary>The requested name is the primary world (<see cref="WellKnownWorlds.Primary"/>), which the database itself rides; it is never droppable and nothing changed.</summary>
    PrimaryWorld
}
