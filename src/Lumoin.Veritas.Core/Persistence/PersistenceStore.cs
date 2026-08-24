using System;
using System.Buffers;
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
/// content or the new content, never a torn mix. <see cref="Read"/> returns an artifact's whole bytes in a
/// heap buffer — the small control-plane reads (the manifest and CURRENT pointer); every bulk-segment read
/// instead goes through an image-source seam so a large segment loads without a transient whole-image heap
/// copy: <see cref="OpenImage"/> when the bytes are not retained past the open scope (a decode, a verify),
/// and <see cref="OpenPooledImage"/> when they are (the system-of-record image a repair reads across the
/// pass, a sketch image returned for a peer).
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

    /// <summary>Reads an artifact's full bytes into a heap buffer — the small control-plane reads (the manifest and CURRENT pointer). Every bulk-segment read goes through <see cref="OpenImage"/> (when the bytes are not retained past the open scope) or <see cref="OpenPooledImage"/> (when they are) instead, to avoid a transient whole-image heap copy.</summary>
    /// <param name="name">The artifact name within the store.</param>
    /// <returns>The bytes, or <see langword="null"/> when no such artifact exists.</returns>
    public abstract byte[]? Read(string name);

    /// <summary>
    /// Opens a memory-efficient image source over a bulk segment artifact whose bytes are NOT retained past the
    /// open scope — a decode (the dictionary, system-of-record, named-graph, and re-derivable columnar-sidecar
    /// loads) or a verify (the scrub pass over every artifact, a parity verify) — so a large segment loads
    /// without a transient whole-image heap copy; a read that DOES retain the bytes uses
    /// <see cref="OpenPooledImage"/> instead. The host backend memory-maps the file (the operating system pages
    /// the image in on demand); a browser or remote backend materialises a pooled or range-fetched buffer behind
    /// the same seam. A memory-mapping backend holds the artifact
    /// mapped for the returned source's lifetime (on some hosts unrenameable and undeletable while mapped),
    /// so the caller disposes the source as soon as the segment is decoded and keeps the open scope tight.
    /// </summary>
    /// <param name="name">The artifact name within the store.</param>
    /// <returns>An image source the caller owns and disposes once the segment is verified and decoded, or <see langword="null"/> when no readable image artifact exists under <paramref name="name"/> (it is absent or not a usable segment image).</returns>
    public abstract SegmentImageSource? OpenImage(string name);

    /// <summary>
    /// Reads a bulk segment artifact into an OWNED, POOLED buffer the caller retains and disposes — the path for a
    /// consumer that holds the bytes past the read, which the (host) memory-mapped <see cref="OpenImage"/> source
    /// cannot safely back: the system-of-record image a parity repair reads across the whole pass, and a sketch
    /// image returned for a peer to load. The bytes are read directly into a buffer rented from
    /// <paramref name="pool"/>, so no transient whole-image heap copy is made; the returned source returns the
    /// buffer to <paramref name="pool"/> on disposal. The image is retainable as
    /// <see cref="PooledSegmentImageSource.ImageMemory"/> for the source's lifetime.
    /// </summary>
    /// <param name="name">The artifact name within the store.</param>
    /// <param name="pool">The pool the image buffer is rented from; the returned source returns it on disposal.</param>
    /// <returns>A pooled image source the caller owns and disposes, or <see langword="null"/> when no readable image artifact exists under <paramref name="name"/>.</returns>
    public abstract PooledSegmentImageSource? OpenPooledImage(string name, MemoryPool<byte> pool);

    /// <summary>Lists the names of the artifacts whose names begin with <paramref name="prefix"/> — the recovery and generation-collection enumeration.</summary>
    /// <param name="prefix">The name prefix to match.</param>
    /// <returns>The matching artifact names.</returns>
    public abstract IReadOnlyList<string> List(string prefix);

    /// <summary>Removes an artifact; a no-op when it does not exist.</summary>
    /// <param name="name">The artifact name within the store.</param>
    public abstract void Delete(string name);
}
