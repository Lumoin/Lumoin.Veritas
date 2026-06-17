using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// The host-filesystem <see cref="PersistenceStore"/>: artifacts are files in a root directory,
/// published by the crash-atomic same-directory rename of <see cref="AtomicPublish"/>. Host-only — a
/// browser runtime plugs in an Origin Private File System store against the same
/// <see cref="PersistenceStore"/> contract instead.
/// </summary>
[UnsupportedOSPlatform("browser")]
public sealed class FileSystemPersistenceStore : PersistenceStore
{
    /// <summary>The root directory holding the artifacts.</summary>
    private readonly string rootDirectory;

    /// <summary>The directory durability barrier applied after a publish; injected so a test can substitute it.</summary>
    private readonly DurabilityBarrierDelegate barrier;

    /// <summary>Creates a store over <paramref name="rootDirectory"/> with the given durability barrier, creating the directory if needed.</summary>
    /// <param name="rootDirectory">The root directory holding the artifacts.</param>
    /// <param name="barrier">The directory durability barrier applied after a publish; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public FileSystemPersistenceStore(string rootDirectory, DurabilityBarrierDelegate barrier)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        ArgumentNullException.ThrowIfNull(barrier);

        this.rootDirectory = rootDirectory;
        this.barrier = barrier;
        Directory.CreateDirectory(rootDirectory);
    }

    /// <summary>Creates a store over <paramref name="rootDirectory"/> with the production durability barrier.</summary>
    /// <param name="rootDirectory">The root directory holding the artifacts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rootDirectory"/> is <see langword="null"/>.</exception>
    public FileSystemPersistenceStore(string rootDirectory)
        : this(rootDirectory, AtomicPublish.DefaultBarrier)
    {
    }

    /// <inheritdoc/>
    public override void WriteStaged(string name, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(name);

        AtomicPublish.WriteDurable(Path.Combine(rootDirectory, name), content);
    }

    /// <inheritdoc/>
    public override void Publish(string stagedName, string finalName)
    {
        ArgumentNullException.ThrowIfNull(stagedName);
        ArgumentNullException.ThrowIfNull(finalName);

        AtomicPublish.Publish(Path.Combine(rootDirectory, stagedName), Path.Combine(rootDirectory, finalName), barrier);
    }

    /// <inheritdoc/>
    public override byte[]? Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string path = Path.Combine(rootDirectory, name);

        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> List(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if(!Directory.Exists(rootDirectory))
        {
            return [];
        }

        List<string> names = [];
        foreach(string path in Directory.EnumerateFiles(rootDirectory))
        {
            string fileName = Path.GetFileName(path);
            if(fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                names.Add(fileName);
            }
        }

        return names;
    }

    /// <inheritdoc/>
    public override void Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string path = Path.Combine(rootDirectory, name);
        if(File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
