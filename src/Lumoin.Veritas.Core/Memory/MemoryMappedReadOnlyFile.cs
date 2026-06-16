using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// A read-only memory-mapped view of a whole file, exposing the mapped bytes as a
/// <see cref="ReadOnlySpan{T}"/> a warm reader decodes in place. The operating system pages the
/// bytes in on demand, so a large image loads without a heap copy of the whole file. The mapping and
/// its acquired pointer are released on <see cref="Dispose"/>, with a finalizer backstop so the view
/// is reclaimed even when an owner is dropped without disposing; the release happens exactly once
/// whichever of dispose or finalize runs first. The view is transient — a caller holds the span only
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

    /// <summary>The number of mapped bytes.</summary>
    private readonly int length;

    /// <summary>The acquired base address of the mapped data, or <see langword="null"/> once released.</summary>
    private byte* pointer;

    /// <summary>Zero until the release runs, one after — the once-guard against the dispose/finalize race.</summary>
    private int released;

    /// <summary>Maps the whole file read-only and acquires its base pointer.</summary>
    /// <param name="handle">The open file handle; left open so the caller keeps ownership.</param>
    /// <param name="length">The file length in bytes; positive and within a span's range.</param>
    private MemoryMappedReadOnlyFile(SafeFileHandle handle, int length)
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

    /// <summary>Maps <paramref name="handle"/>'s whole content read-only.</summary>
    /// <param name="handle">The open, readable file handle; left open so the caller keeps ownership.</param>
    /// <param name="length">The file length in bytes; positive and within a span's range.</param>
    /// <returns>The mapped view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive or exceeds a span's range.</exception>
    public static MemoryMappedReadOnlyFile Open(SafeFileHandle handle, long length)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, int.MaxValue);

        return new MemoryMappedReadOnlyFile(handle, (int)length);
    }

    /// <summary>The mapped bytes as a read-only span; valid only until <see cref="Dispose"/>.</summary>
    /// <exception cref="ObjectDisposedException">The view has been released.</exception>
    public ReadOnlySpan<byte> Span
    {
        get
        {
            byte* current = pointer;
            ObjectDisposedException.ThrowIf(current is null, this);

            return new ReadOnlySpan<byte>(current, length);
        }
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
