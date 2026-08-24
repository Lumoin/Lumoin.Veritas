using System;
using System.Runtime.Versioning;
using Lumoin.Veritas.Core.Memory;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// A <see cref="SegmentImageSource"/> backed by a read-only memory-mapped file: the operating system
/// pages the image in on demand, so a large segment loads without a heap copy of the whole image. The
/// mapping is released on <see cref="Dispose()"/>. Host-only — a browser runtime has no file to map.
/// </summary>
[UnsupportedOSPlatform("browser")]
public sealed class MemoryMappedSegmentImageSource : SegmentImageSource
{
    /// <summary>The mapped view of the file.</summary>
    private readonly MemoryMappedReadOnlyFile mapped;

    /// <summary>The file handle this source opened and disposes with the mapping, or <see langword="null"/> when the handle is borrowed and the caller keeps ownership.</summary>
    private readonly SafeFileHandle? ownedHandle;

    /// <summary>Wraps an open mapped view, optionally owning the handle it was mapped from.</summary>
    /// <param name="mapped">The mapped view.</param>
    /// <param name="ownedHandle">The handle to dispose with the mapping, or <see langword="null"/> when it is borrowed.</param>
    private MemoryMappedSegmentImageSource(MemoryMappedReadOnlyFile mapped, SafeFileHandle? ownedHandle)
    {
        this.mapped = mapped;
        this.ownedHandle = ownedHandle;
    }

    /// <summary>Maps <paramref name="handle"/>'s whole content read-only as an image source, leaving the handle open for the caller, which keeps ownership of it.</summary>
    /// <param name="handle">The open, readable file handle; left open so the caller keeps ownership.</param>
    /// <param name="length">The file length in bytes; positive.</param>
    /// <returns>The mapped image source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive.</exception>
    public static MemoryMappedSegmentImageSource Open(SafeFileHandle handle, long length)
    {
        return new MemoryMappedSegmentImageSource(MemoryMappedReadOnlyFile.Open(handle, length), ownedHandle: null);
    }

    /// <summary>Maps <paramref name="handle"/>'s whole content read-only and takes ownership of the handle, disposing it with the mapping — the form a persistence store hands back from its image-open seam, where the source outlives the open call. All-or-nothing: a mapping failure disposes the handle before propagating, so the handle is never leaked.</summary>
    /// <param name="handle">The open, readable file handle; this source takes ownership and disposes it with the mapping.</param>
    /// <param name="length">The file length in bytes; positive.</param>
    /// <returns>The mapped image source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive.</exception>
    public static MemoryMappedSegmentImageSource OpenOwning(SafeFileHandle handle, long length)
    {
        ArgumentNullException.ThrowIfNull(handle);

        try
        {
            return new MemoryMappedSegmentImageSource(MemoryMappedReadOnlyFile.Open(handle, length), handle);
        }
        catch
        {
            handle.Dispose();

            throw;
        }
    }

    /// <inheritdoc/>
    public override long Length => mapped.Length;

    /// <inheritdoc/>
    public override ReadOnlySpan<byte> Slice(long offset, int length) => mapped.Slice(offset, length);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            mapped.Dispose();
            ownedHandle?.Dispose();
        }
    }
}
