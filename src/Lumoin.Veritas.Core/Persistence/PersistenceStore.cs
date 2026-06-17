using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// The open extension point for the durable named-artifact store the persistence layer commits into:
/// a flat namespace of named files with a single crash-atomic publish operation, decoupled from the
/// platform that provides it. The host filesystem backend (<see cref="FileSystemPersistenceStore"/>)
/// is built in; a browser runtime plugs in its own over the Origin Private File System, and a remote
/// or object-store deployment plugs in its own — the manifest, CURRENT pointer, and recovery logic
/// depend only on this abstraction, never on a platform's file API, so the same commit model runs
/// everywhere.
/// </summary>
/// <remarks>
/// <para>
/// The contract is deliberately small — write a staged artifact durably, atomically publish it under
/// a final name, read, list, delete — because that is exactly what the immutable-generation +
/// atomic-CURRENT-publish model needs and exactly what every backend (POSIX/Windows rename, OPFS
/// <c>move</c>, an object store's conditional put) can provide. <see cref="Publish"/> is the single
/// commit point: it must make the staged artifact live atomically, so a crash leaves either the prior
/// content or the new content, never a torn mix. This store carries small control-plane artifacts
/// (the manifest and CURRENT pointer); bulk data segments are read through the
/// <see cref="Lumoin.Veritas.Core.Columnar.ColumnarImageSource"/> seam.
/// </para>
/// </remarks>
public abstract class PersistenceStore
{
    /// <summary>Writes a staged (not-yet-live) artifact and flushes it to stable storage before returning.</summary>
    /// <param name="name">The artifact name within the store.</param>
    /// <param name="content">The bytes to write.</param>
    public abstract void WriteStaged(string name, ReadOnlySpan<byte> content);

    /// <summary>Atomically makes a previously staged artifact live under <paramref name="finalName"/> — the single commit point. A crash leaves either the prior content or the new, never a torn mix.</summary>
    /// <param name="stagedName">The staged artifact written by <see cref="WriteStaged"/>.</param>
    /// <param name="finalName">The name the staged artifact becomes live under.</param>
    public abstract void Publish(string stagedName, string finalName);

    /// <summary>Reads an artifact's full bytes.</summary>
    /// <param name="name">The artifact name within the store.</param>
    /// <returns>The bytes, or <see langword="null"/> when no such artifact exists.</returns>
    public abstract byte[]? Read(string name);

    /// <summary>Lists the names of the artifacts whose names begin with <paramref name="prefix"/> — the recovery and generation-collection enumeration.</summary>
    /// <param name="prefix">The name prefix to match.</param>
    /// <returns>The matching artifact names.</returns>
    public abstract IReadOnlyList<string> List(string prefix);

    /// <summary>Removes an artifact; a no-op when it does not exist.</summary>
    /// <param name="name">The artifact name within the store.</param>
    public abstract void Delete(string name);
}
