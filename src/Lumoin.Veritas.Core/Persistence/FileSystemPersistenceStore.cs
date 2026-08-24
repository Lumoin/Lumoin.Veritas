using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

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

    /// <summary>The directory durability barrier applied after a publish; injected so a consumer can wire a platform-specific barrier and a test can substitute it.</summary>
    private readonly DurabilityBarrierDelegate barrier;

    /// <summary>The file-content durability flush applied to a staged write; injected so a consumer can wire a platform-specific flush and a test can substitute it.</summary>
    private readonly DurableFlushDelegate flush;

    /// <summary>Creates a store over <paramref name="rootDirectory"/> with the given durability seams, creating the directory if needed.</summary>
    /// <param name="rootDirectory">The root directory holding the artifacts.</param>
    /// <param name="barrier">The directory durability barrier applied after a publish; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <param name="flush">The file-content durability flush applied to a staged write; pass <see cref="AtomicPublish.DefaultFlush"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public FileSystemPersistenceStore(string rootDirectory, DurabilityBarrierDelegate barrier, DurableFlushDelegate flush)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        ArgumentNullException.ThrowIfNull(barrier);
        ArgumentNullException.ThrowIfNull(flush);

        this.rootDirectory = rootDirectory;
        this.barrier = barrier;
        this.flush = flush;
        Directory.CreateDirectory(rootDirectory);
    }

    /// <summary>Creates a store over <paramref name="rootDirectory"/> with the given directory barrier and the production file-content flush, creating the directory if needed.</summary>
    /// <param name="rootDirectory">The root directory holding the artifacts.</param>
    /// <param name="barrier">The directory durability barrier applied after a publish; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public FileSystemPersistenceStore(string rootDirectory, DurabilityBarrierDelegate barrier)
        : this(rootDirectory, barrier, AtomicPublish.DefaultFlush)
    {
    }

    /// <summary>Creates a store over <paramref name="rootDirectory"/> with the production durability seams.</summary>
    /// <param name="rootDirectory">The root directory holding the artifacts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rootDirectory"/> is <see langword="null"/>.</exception>
    public FileSystemPersistenceStore(string rootDirectory)
        : this(rootDirectory, AtomicPublish.DefaultBarrier, AtomicPublish.DefaultFlush)
    {
    }

    /// <inheritdoc/>
    public override void WriteStaged(string name, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(name);

        AtomicPublish.WriteDurable(Path.Combine(rootDirectory, name), content, flush);
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
    public override SegmentImageSource? OpenImage(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string path = Path.Combine(rootDirectory, name);

        SafeFileHandle? handle = null;
        try
        {
            try
            {
                //FileShare.Delete lets a concurrent collect/publish proceed against the read handle; note a live
                //memory-mapped section still independently holds the name on some hosts, so when concurrent
                //collection lands, retention must also avoid collecting a generation under an active load.
                handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch(FileNotFoundException)
            {
                return null;
            }
            catch(DirectoryNotFoundException)
            {
                return null;
            }

            long length = RandomAccess.GetLength(handle);

            //A zero-length artifact is not a loadable segment image; returning null reads as a
            //missing-or-rejected artifact on the load path. Size carries no ceiling here: the mapped source
            //addresses any length through bounded windows, and a reader that needs the whole image in one span
            //fails loudly at the Image accessor rather than being refused a source.
            if(length is 0)
            {
                return null;
            }

            //Ownership of the handle transfers to the source, which disposes it with the mapping; clearing the local
            //hands cleanup to the source so the finally below does not also dispose it. The finally is the backstop
            //for every earlier path (a length read that throws, an over-size or empty artifact).
            MemoryMappedSegmentImageSource source = MemoryMappedSegmentImageSource.OpenOwning(handle, length);
            handle = null;

            return source;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <inheritdoc/>
    public override PooledSegmentImageSource? OpenPooledImage(string name, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(pool);

        string path = Path.Combine(rootDirectory, name);

        SafeFileHandle? handle = null;
        try
        {
            try
            {
                handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch(FileNotFoundException)
            {
                return null;
            }
            catch(DirectoryNotFoundException)
            {
                return null;
            }

            long length = RandomAccess.GetLength(handle);

            //An empty, over-cap, or (below) short-read artifact is not a loadable POOLED image; returning null
            //reads as a missing-or-rejected artifact on the verify/repair and sketch-load paths. The cap here is
            //intrinsic to pooling — a pooled image lives in ONE rented buffer — not a file-size ceiling; a larger
            //artifact reads through the mapped, windowed source instead.
            if(length is 0 || length > Array.MaxLength)
            {
                return null;
            }

            int imageLength = (int)length;
            IMemoryOwner<byte> owner = pool.Rent(imageLength);
            try
            {
                if(!TryFillFromFile(handle, owner.Memory.Span[..imageLength]))
                {
                    owner.Dispose();

                    return null;
                }
            }
            catch
            {
                owner.Dispose();

                throw;
            }

            //The pooled buffer holds the whole image independently of the handle, so the handle is released in the
            //finally while the returned source keeps only the rented buffer, which it returns to the pool on dispose.
            return new PooledSegmentImageSource(owner, imageLength);
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes from the start of the file, looping over partial positional reads.</summary>
    /// <param name="handle">The open file handle.</param>
    /// <param name="destination">The buffer to fill.</param>
    /// <returns><see langword="true"/> when the buffer was filled; <see langword="false"/> when the file ended early (a concurrent truncation), so the caller refuses it.</returns>
    private static bool TryFillFromFile(SafeFileHandle handle, Span<byte> destination)
    {
        int filled = 0;
        while(filled < destination.Length)
        {
            int read = RandomAccess.Read(handle, destination[filled..], filled);
            if(read == 0)
            {
                return false;
            }

            filled += read;
        }

        return true;
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
