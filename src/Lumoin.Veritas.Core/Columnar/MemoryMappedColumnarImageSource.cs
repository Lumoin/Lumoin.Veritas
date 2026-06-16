using System;
using System.Runtime.Versioning;
using Lumoin.Veritas.Core.Memory;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A <see cref="ColumnarImageSource"/> backed by a read-only memory-mapped file: the operating system
/// pages the image in on demand, so a large index loads without a heap copy of the whole image. The
/// mapping is released on <see cref="Dispose()"/>. Host-only — a browser runtime has no file to map.
/// </summary>
[UnsupportedOSPlatform("browser")]
public sealed class MemoryMappedColumnarImageSource : ColumnarImageSource
{
    /// <summary>The mapped view of the file.</summary>
    private readonly MemoryMappedReadOnlyFile mapped;

    /// <summary>Wraps an open mapped view.</summary>
    /// <param name="mapped">The mapped view.</param>
    private MemoryMappedColumnarImageSource(MemoryMappedReadOnlyFile mapped)
    {
        this.mapped = mapped;
    }

    /// <summary>Maps <paramref name="handle"/>'s whole content read-only as an image source.</summary>
    /// <param name="handle">The open, readable file handle; left open so the caller keeps ownership.</param>
    /// <param name="length">The file length in bytes; positive and within a span's range.</param>
    /// <returns>The mapped image source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive or exceeds a span's range.</exception>
    public static MemoryMappedColumnarImageSource Open(SafeFileHandle handle, long length)
    {
        return new MemoryMappedColumnarImageSource(MemoryMappedReadOnlyFile.Open(handle, length));
    }

    /// <inheritdoc/>
    public override ReadOnlySpan<byte> Image => mapped.Span;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            mapped.Dispose();
        }
    }
}
