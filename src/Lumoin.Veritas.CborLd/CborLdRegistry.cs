using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// An in-memory CBOR-LD registry: a small dictionary of
/// <see cref="CborLdRegistryEntry"/> values keyed by registry-entry id.
/// Use this when the registry entries are known up front (compiled in or
/// loaded once at startup). For larger or dynamically loaded registries
/// supply a <see cref="LoadCborLdRegistryEntryDelegate"/> instead.
/// </summary>
public sealed class CborLdRegistry
{
    private readonly Dictionary<int, CborLdRegistryEntry> entries;

    /// <summary>
    /// Initialises a registry containing the supplied entries plus the
    /// passthrough entry (id <c>0</c>). If <paramref name="entries"/>
    /// already contains an entry with id <c>0</c>, that one wins.
    /// </summary>
    /// <param name="entries">Entries to register. May be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <c>null</c>.</exception>
    public CborLdRegistry(IEnumerable<CborLdRegistryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        this.entries = new Dictionary<int, CborLdRegistryEntry>
        {
            [0] = CborLdRegistryEntry.Passthrough
        };

        foreach(CborLdRegistryEntry entry in entries)
        {
            this.entries[entry.RegistryEntryId] = entry;
        }
    }

    /// <summary>
    /// Gets a registry containing only the passthrough entry.
    /// </summary>
    public static CborLdRegistry Empty { get; } = new([]);

    /// <summary>Gets the registered entries by id.</summary>
    public IReadOnlyDictionary<int, CborLdRegistryEntry> Entries => entries;

    /// <summary>
    /// Attempts to look up the entry registered under
    /// <paramref name="registryEntryId"/>.
    /// </summary>
    /// <param name="registryEntryId">The id to look up.</param>
    /// <param name="entry">Receives the entry on success.</param>
    /// <returns><c>true</c> when the id is registered; <c>false</c> otherwise.</returns>
    public bool TryGet(int registryEntryId, out CborLdRegistryEntry? entry)
    {
        return entries.TryGetValue(registryEntryId, out entry);
    }

    /// <summary>
    /// Adapts this in-memory registry to a
    /// <see cref="LoadCborLdRegistryEntryDelegate"/> for callers that
    /// expect the delegate-shaped lookup boundary.
    /// </summary>
    /// <returns>A delegate that resolves against this registry's entries.</returns>
    public LoadCborLdRegistryEntryDelegate AsDelegate()
    {
        return ResolveAsync;
    }

    private ValueTask<CborLdRegistryEntry?> ResolveAsync(int registryEntryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entries.TryGetValue(registryEntryId, out CborLdRegistryEntry? entry);
        return ValueTask.FromResult(entry);
    }
}
