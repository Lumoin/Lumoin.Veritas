using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// The crash-atomic file-publish primitives the persistence commit point is built on: write a staged
/// file durably, then make it live by an atomic same-directory rename followed by a directory
/// durability barrier. The single commit point of a manifest generation is one such publish of the
/// CURRENT pointer; everything else a generation needs is staged and flushed beforehand, so a crash
/// before the publish leaves the prior committed state wholly in force.
/// </summary>
/// <remarks>
/// <para>
/// Host-only: there is no file system to publish into in a browser runtime. The atomic rename relies
/// on staging the temp file in the SAME directory as its target — a same-volume rename is atomic,
/// whereas a cross-volume move silently degrades to copy-then-delete and is not — so
/// <see cref="Publish"/> enforces same-directory staging rather than trusting the caller. The
/// directory barrier (<see cref="DefaultBarrier"/>) issues the parent-directory <c>fsync</c> that
/// .NET exposes no managed API for; it is a guarded host-only call on Linux and macOS (mirroring the
/// firmware-probe precedent) and a no-op on Windows, and is injected as a
/// <see cref="DurabilityBarrierDelegate"/> so a test can substitute it. Its effectiveness is bounded
/// by the underlying file system: a backend that does not honour <c>fsync</c> (some network or
/// overlay file systems, lying virtual disks) leaves a residual that the retained prior commit
/// generations and the degraded recovery scan cover.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("browser")]
public static class AtomicPublish
{
    /// <summary>The POSIX <c>open</c> flag for a read-only handle; 0 on Linux and macOS, enough to obtain a directory descriptor to flush.</summary>
    private const int ReadOnlyFlag = 0;

    /// <summary>The production durability barrier: a parent-directory <c>fsync</c> on Linux and macOS, a no-op on Windows.</summary>
    public static DurabilityBarrierDelegate DefaultBarrier { get; } = FlushDirectoryMetadata;

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/> and flushes it to stable storage before returning, so the bytes survive a power loss.</summary>
    /// <param name="path">The file to write; created or overwritten.</param>
    /// <param name="content">The bytes to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">The write or flush failed.</exception>
    public static void WriteDurable(string path, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Makes <paramref name="stagedPath"/> live at <paramref name="finalPath"/> by an atomic rename, then flushes the target directory through <paramref name="barrier"/> — the single commit point.</summary>
    /// <param name="stagedPath">The already-written, already-flushed staged file; must sit in the same directory as <paramref name="finalPath"/>.</param>
    /// <param name="finalPath">The target name made live by the rename.</param>
    /// <param name="barrier">The directory durability barrier; pass <see cref="DefaultBarrier"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The staged file and the target are not in the same directory (a cross-volume rename is not atomic).</exception>
    /// <exception cref="IOException">The rename failed.</exception>
    public static void Publish(string stagedPath, string finalPath, DurabilityBarrierDelegate barrier)
    {
        ArgumentNullException.ThrowIfNull(stagedPath);
        ArgumentNullException.ThrowIfNull(finalPath);
        ArgumentNullException.ThrowIfNull(barrier);

        string? stagedDirectory = Path.GetDirectoryName(Path.GetFullPath(stagedPath));
        string? finalDirectory = Path.GetDirectoryName(Path.GetFullPath(finalPath));
        if(!string.Equals(stagedDirectory, finalDirectory, StringComparison.Ordinal))
        {
            throw new ArgumentException("The staged file must be in the same directory as its target so the publish is an atomic same-volume rename.", nameof(stagedPath));
        }

        File.Move(stagedPath, finalPath, overwrite: true);
        if(finalDirectory is not null)
        {
            barrier(finalDirectory);
        }
    }

    /// <summary>Flushes a directory's metadata to disk on the platforms that expose it: Linux and macOS via a guarded <c>open</c>+<c>fsync</c>+<c>close</c>, Windows and others a no-op.</summary>
    /// <param name="directoryPath">The directory whose metadata is flushed.</param>
    /// <exception cref="IOException">The directory could not be opened or flushed.</exception>
    private static void FlushDirectoryMetadata(string directoryPath)
    {
        if(!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(directoryPath);
        byte[] nullTerminated = new byte[pathBytes.Length + 1];
        pathBytes.CopyTo(nullTerminated, 0);

        int descriptor = Open(nullTerminated, ReadOnlyFlag);
        if(descriptor < 0)
        {
            throw new IOException($"Could not open the directory '{directoryPath}' to flush its metadata (errno {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if(Fsync(descriptor) != 0)
            {
                throw new IOException($"Could not flush the directory '{directoryPath}' to disk (errno {Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            _ = Close(descriptor);
        }
    }

    /// <summary>Opens a path and returns a file descriptor, or a negative value on error.</summary>
    /// <param name="pathname">The null-terminated UTF-8 path.</param>
    /// <param name="flags">The open flags.</param>
    /// <returns>The file descriptor, or a negative value on error.</returns>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint ="open", SetLastError = true)]
    private static extern int Open(byte[] pathname, int flags);

    /// <summary>Flushes a descriptor's file (or directory) to stable storage.</summary>
    /// <param name="descriptor">The file descriptor.</param>
    /// <returns>0 on success, a negative value on error.</returns>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint ="fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    /// <summary>Closes a file descriptor.</summary>
    /// <param name="descriptor">The file descriptor.</param>
    /// <returns>0 on success, a negative value on error.</returns>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint ="close", SetLastError = true)]
    private static extern int Close(int descriptor);
}
