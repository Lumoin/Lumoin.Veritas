namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The outcome of forking a named world in a <see cref="DatasetWorlds"/> registry. Name collisions and
/// unknown sources are expected registry conditions — two callers race the same fork name, an editor
/// drops a world another surface still addresses — so they answer as values rather than exceptions.
/// </summary>
public enum WorldForkOutcome
{
    /// <summary>The fork was created and registered under the requested name.</summary>
    Forked,

    /// <summary>No world is registered under the requested source name; nothing was forked.</summary>
    UnknownSource,

    /// <summary>A world is already registered under the requested fork name; nothing was registered. A racing fork that lost the registration also answers this — the loser's world is discarded before it is ever returned.</summary>
    DuplicateName
}
