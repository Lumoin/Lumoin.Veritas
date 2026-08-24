using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// A registry of named WORLDS — independently evolving
/// <see cref="MutableSparqlDataset"/> instances forked from one
/// another over one shared term dictionary and node arena. Each
/// world is a linear journal with its own head; the journals form a
/// DAG through <see cref="Lumoin.Veritas.Core.Hypertrie.Editing.EditSessionEntryKind.Forked"/>
/// edges, and the registry records each world's fork parent by name
/// so a reader can present that lineage. A world's name is its
/// identity: dataset state identifiers are content-addressed, so
/// two worlds that converge to identical content share one state
/// identifier and cannot be told apart by state alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> Dropping a world removes its NAME from the
/// registry; the world object, its journal, and its private states
/// live on for exactly as long as outside holders reference them,
/// after which the arena's weak registries make the world's
/// unshared roots sweepable. Nodes shared with surviving worlds are
/// untouched — content addressing keeps them interned once for
/// everyone. A surviving fork keeps its recorded parent NAME even
/// when the parent's name is dropped: lineage is history, not a
/// live reference.
/// </para>
/// <para>
/// <b>Thread safety.</b> The registry is safe for concurrent forks,
/// drops, and lookups. Two concurrent forks under one new name race
/// at the registration; the loser's world is discarded before it is
/// ever returned, which leaks nothing — the fork holds no resources
/// beyond its journal's fork entry.
/// </para>
/// </remarks>
public sealed class DatasetWorlds
{
    /// <summary>One name's registration: the world and the name of the world it was forked from (<see langword="null"/> for the seed world).</summary>
    private readonly record struct WorldRegistration(MutableSparqlDataset World, string? Parent);

    /// <summary>The registrations by world name.</summary>
    private ConcurrentDictionary<string, WorldRegistration> Worlds { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Constructs a registry seeded with one world, which carries no fork parent.
    /// </summary>
    /// <param name="name">The seed world's name.</param>
    /// <param name="world">The seed world.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="world"/> is <see langword="null"/>.</exception>
    public DatasetWorlds(string name, MutableSparqlDataset world)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(world);

        Worlds[name] = new WorldRegistration(world, null);
    }

    /// <summary>The names of the registered worlds.</summary>
    public IReadOnlyCollection<string> Names => [.. Worlds.Keys];

    /// <summary>Looks up a world by name.</summary>
    /// <param name="name">The world's name.</param>
    /// <param name="world">Receives the world on success.</param>
    /// <returns><see langword="true"/> when the world exists.</returns>
    public bool TryGet(string name, out MutableSparqlDataset? world)
    {
        ArgumentNullException.ThrowIfNull(name);

        bool found = Worlds.TryGetValue(name, out WorldRegistration registered);
        world = found ? registered.World : null;

        return found;
    }

    /// <summary>
    /// Snapshots the registered worlds as entries carrying each world's name, its fork parent's name,
    /// and the world itself. The snapshot is point-in-time and unordered — a presenting caller applies
    /// its own ordering — and a concurrent fork or drop is either wholly in or wholly out of it.
    /// </summary>
    /// <returns>The registered worlds, unordered.</returns>
    public ImmutableArray<DatasetWorldEntry> Describe()
    {
        ImmutableArray<DatasetWorldEntry>.Builder entries = ImmutableArray.CreateBuilder<DatasetWorldEntry>(Worlds.Count);
        foreach(KeyValuePair<string, WorldRegistration> registration in Worlds)
        {
            entries.Add(new DatasetWorldEntry(registration.Key, registration.Value.Parent, registration.Value.World));
        }

        return entries.DrainToImmutable();
    }

    /// <summary>
    /// Forks a source world's current committed state into a new
    /// world registered under <paramref name="forkName"/>, recording
    /// <paramref name="sourceName"/> as the fork's parent. The fork
    /// gets a fresh in-memory journal; see
    /// <see cref="MutableSparqlDataset.ForkAsync"/> for the sharing
    /// and isolation contract. An unknown source and a taken fork
    /// name are expected conditions and answer as outcomes; a
    /// racing fork that loses the registration answers
    /// <see cref="WorldForkOutcome.DuplicateName"/> and its world
    /// is discarded before it is ever returned, which leaks
    /// nothing — the fork holds no resources beyond its journal's
    /// fork entry.
    /// </summary>
    /// <param name="sourceName">The world to fork from.</param>
    /// <param name="forkName">The new world's name.</param>
    /// <param name="cancellationToken">A token that aborts the fork.</param>
    /// <returns>The outcome, carrying the forked world on <see cref="WorldForkOutcome.Forked"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceName"/> or <paramref name="forkName"/> is <see langword="null"/>.</exception>
    public async ValueTask<WorldFork> TryForkAsync(string sourceName, string forkName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(forkName);

        if(!Worlds.TryGetValue(sourceName, out WorldRegistration source))
        {
            return new WorldFork(WorldForkOutcome.UnknownSource, null);
        }

        if(Worlds.ContainsKey(forkName))
        {
            return new WorldFork(WorldForkOutcome.DuplicateName, null);
        }

        MutableSparqlDataset fork = await source.World.ForkAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        if(!Worlds.TryAdd(forkName, new WorldRegistration(fork, sourceName)))
        {
            return new WorldFork(WorldForkOutcome.DuplicateName, null);
        }

        return new WorldFork(WorldForkOutcome.Forked, fork);
    }

    /// <summary>
    /// Removes a world's name from the registry. The world object
    /// stays usable by existing holders; once unreferenced, its
    /// unshared roots become sweepable through the arena's weak
    /// registries.
    /// </summary>
    /// <param name="name">The world's name.</param>
    /// <returns><see langword="true"/> when a world was registered under the name.</returns>
    public bool Drop(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Worlds.TryRemove(name, out _);
    }
}
