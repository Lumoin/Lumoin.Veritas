using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// A read-only memory mapping of a whole file, exposing bounded windows over the mapped bytes at
/// <see cref="long"/> offsets, so files larger than a single span's range read without a heap copy:
/// the operating system pages bytes in on demand and a reader slices one window per read. The mapping
/// and its acquired pointer are released on <see cref="Dispose"/>, with a finalizer backstop so the
/// view is reclaimed even when an owner is dropped without disposing; the release happens exactly once
/// whichever of dispose or finalize runs first. A window is transient — a caller holds the span only
/// for the duration of one read, never across the point where the owner is released — so a decoder
/// copies what it needs out of the span before disposal.
/// </summary>
/// <remarks>
/// <para>
/// Memory mapping is unavailable in a browser runtime, so this type is host-only. The acquired view
/// pointer is released against the <see cref="SafeMemoryMappedViewHandle"/> directly, so the finalizer
/// path touches only a finalizer-safe handle.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("browser")]
internal sealed unsafe class MemoryMappedReadOnlyFile : IDisposable
{
    /// <summary>The mapped file.</summary>
    private readonly MemoryMappedFile file;

    /// <summary>The read-only view over the whole mapped file.</summary>
    private readonly MemoryMappedViewAccessor view;

    /// <summary>The view's safe handle, against which the acquired pointer is released — finalizer-safe.</summary>
    private readonly SafeMemoryMappedViewHandle viewHandle;

    /// <summary>The number of mapped bytes; a <see cref="long"/> because a mapped file may exceed a single span's range.</summary>
    private readonly long length;

    /// <summary>The acquired base address of the mapped data, or <see langword="null"/> once released.</summary>
    private byte* pointer;

    /// <summary>Zero until the release runs, one after — the once-guard against the dispose/finalize race.</summary>
    private int released;

    /// <summary>Maps the whole file read-only and acquires its base pointer.</summary>
    /// <param name="handle">The open file handle; left open so the caller keeps ownership.</param>
    /// <param name="length">The file length in bytes; positive.</param>
    private MemoryMappedReadOnlyFile(SafeFileHandle handle, long length)
    {
        this.length = length;

        MemoryMappedFile? mappedFile = null;
        MemoryMappedViewAccessor? viewAccessor = null;
        try
        {
            mappedFile = MemoryMappedFile.CreateFromFile(handle, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            viewAccessor = mappedFile.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
            SafeMemoryMappedViewHandle safeViewHandle = viewAccessor.SafeMemoryMappedViewHandle;

            byte* basePointer = null;
            safeViewHandle.AcquirePointer(ref basePointer);

            file = mappedFile;
            view = viewAccessor;
            viewHandle = safeViewHandle;
            pointer = basePointer + viewAccessor.PointerOffset;
        }
        catch
        {
            //Whatever was created before the failure is disposed deterministically rather than left to its own finalizer.
            viewAccessor?.Dispose();
            mappedFile?.Dispose();
            throw;
        }
    }

    /// <summary>The mapped length in bytes.</summary>
    public long Length => length;

    /// <summary>Maps <paramref name="handle"/>'s whole content read-only.</summary>
    /// <param name="handle">The open, readable file handle; left open so the caller keeps ownership.</param>
    /// <param name="length">The file length in bytes; positive.</param>
    /// <returns>The mapped view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive.</exception>
    public static MemoryMappedReadOnlyFile Open(SafeFileHandle handle, long length)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        return new MemoryMappedReadOnlyFile(handle, length);
    }

    /// <summary>
    /// Returns a read-only window over the mapped bytes; valid only until <see cref="Dispose"/>. The
    /// offset is a <see cref="long"/> so a reader addresses past a single span's range; the window
    /// itself is span-bounded, which every block-structured format already guarantees per block.
    /// </summary>
    /// <param name="offset">The zero-based byte offset of the window.</param>
    /// <param name="windowLength">The window length in bytes.</param>
    /// <returns>A span over exactly <paramref name="windowLength"/> bytes at <paramref name="offset"/>.</returns>
    /// <exception cref="ObjectDisposedException">The view has been released.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The window does not lie wholly within the mapping.</exception>
    public ReadOnlySpan<byte> Slice(long offset, int windowLength)
    {
        byte* current = pointer;
        ObjectDisposedException.ThrowIf(current is null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(windowLength);
        if(offset > length - windowLength)
        {
            throw new ArgumentOutOfRangeException(nameof(windowLength), windowLength, $"A window of {windowLength} bytes at offset {offset} does not fit a mapping of {length} bytes.");
        }

        return new ReadOnlySpan<byte>(current + offset, windowLength);
    }

    /// <summary>Releases the acquired pointer and the mapping; idempotent and safe against the finalizer.</summary>
    public void Dispose()
    {
        Release(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Reclaims the mapping when <see cref="Dispose"/> was not called.</summary>
    ~MemoryMappedReadOnlyFile()
    {
        Release(disposing: false);
    }

    /// <summary>Releases the acquired pointer exactly once, then disposes the managed wrappers when called deterministically; the finalizer leaves the view's and file's own handles to their finalizers once the pointer is released.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose"/>; <see langword="false"/> from the finalizer.</param>
    private void Release(bool disposing)
    {
        if(Interlocked.Exchange(ref released, 1) != 0)
        {
            return;
        }

        try
        {
            if(pointer is not null)
            {
                pointer = null;
                viewHandle.ReleasePointer();
            }
        }
        finally
        {
            if(disposing)
            {
                view.Dispose();
                file.Dispose();
            }
        }
    }
}
