using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

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
/// .NET exposes no managed API for; it is a guarded host-only call on Linux and the Apple platforms
/// (macOS, iOS, tvOS, Mac Catalyst, mirroring the firmware-probe precedent) and a no-op on Windows, and
/// is injected as a <see cref="DurabilityBarrierDelegate"/> so a test can substitute it. The file bytes
/// a durable write commits get the stronger <c>fcntl(F_FULLFSYNC)</c> on the Apple mobile platforms,
/// where the runtime's own flush degrades to a plain <c>fsync</c> that does not reach stable storage.
/// Effectiveness is bounded by the underlying file system: a backend that does not honour the flush
/// (some network or overlay file systems, lying virtual disks) leaves a residual that the retained prior
/// commit generations and the degraded recovery scan cover.
/// </para>
/// <para>
/// What each host gives by default, at the barrier seam:
/// </para>
/// <para>
/// <b>Windows (NTFS).</b> The file-content flush is the runtime's <c>Flush(flushToDisk)</c> /
/// <see cref="RandomAccess.FlushToDisk"/>, which natively issues <c>FlushFileBuffers</c> — a real
/// device-cache flush. A rename is crash-consistent through the NTFS metadata journal, but its
/// durability point is that journal's own flush, and there is no public directory-fsync API, so the
/// directory barrier is a documented no-op: the commit acknowledgement can precede the rename's
/// durability, and a power loss shortly after reverts to the prior committed generation, left wholly
/// intact.
/// </para>
/// <para>
/// <b>Linux (including Android).</b> <c>fsync</c> flushes file data and metadata and issues a device
/// cache flush (journal barriers are on by default); rename durability additionally requires an
/// <c>fsync</c> on the parent directory, which is exactly what the barrier delegate does there.
/// </para>
/// <para>
/// <b>macOS, iOS, tvOS, Mac Catalyst (APFS).</b> A plain <c>fsync</c> reaches only the drive's cache;
/// durable-to-media needs <c>fcntl(F_FULLFSYNC)</c>, so the default flush issues <c>F_FULLFSYNC</c>
/// directly on the Apple mobile platforms, where the runtime flush degrades to a plain <c>fsync</c>. The
/// directory barrier stays a best-effort <c>fsync</c> there.
/// </para>
/// <para>
/// <b>Browser/WASM (OPFS).</b> There is no publish path here — this type is unsupported in a browser
/// runtime. Where the same durability vocabulary is used over the origin's private file system, flushing
/// persists into the origin's evictable storage bucket, best-effort, with no media guarantee: a
/// durability claim there means survives-tab-close, not survives-power-loss or eviction.
/// </para>
/// <para>
/// <see cref="FileOptions.WriteThrough"/> is not a portable substitute for the flush delegate: it maps
/// to <c>FILE_FLAG_WRITE_THROUGH</c> on Windows and <c>O_SYNC</c> on Unix, and <c>O_SYNC</c> on the
/// Apple platforms still does not imply <c>F_FULLFSYNC</c>.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("browser")]
public static class AtomicPublish
{
    /// <summary>The POSIX <c>open</c> flag for a read-only handle; 0 on Linux and macOS, enough to obtain a directory descriptor to flush.</summary>
    private const int ReadOnlyFlag = 0;

    /// <summary>The Darwin <c>fcntl</c> command that asks the drive to flush its write cache to permanent storage — the durability guarantee plain <c>fsync</c> does not give on Apple platforms.</summary>
    private const int FullFsync = 51;

    /// <summary>The production durability barrier: a parent-directory <c>fsync</c> on Linux and the Apple platforms, a no-op on Windows.</summary>
    public static DurabilityBarrierDelegate DefaultBarrier { get; } = FlushDirectoryMetadata;

    /// <summary>The production file-content flush: <c>fcntl(F_FULLFSYNC)</c> on the Apple mobile platforms where the runtime flush degrades to a plain <c>fsync</c>, the runtime flush everywhere else.</summary>
    public static DurableFlushDelegate DefaultFlush { get; } = FlushFull;

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/> and flushes it to stable storage through <paramref name="flush"/> before returning, so the bytes survive a power loss.</summary>
    /// <param name="path">The file to write; created or overwritten.</param>
    /// <param name="content">The bytes to write.</param>
    /// <param name="flush">The file-content durability flush; pass <see cref="DefaultFlush"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="IOException">The write or flush failed.</exception>
    public static void WriteDurable(string path, ReadOnlySpan<byte> content, DurableFlushDelegate flush)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(flush);

        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None);
        RandomAccess.Write(handle, content, 0);
        flush(handle);
    }

    /// <summary>
    /// Flushes a written file to stable storage so its bytes survive a power
    /// loss. On the Apple mobile platforms (iOS, tvOS, Mac Catalyst) the .NET
    /// runtime issues only a plain <c>fsync</c> — it upgrades to
    /// <c>F_FULLFSYNC</c> only under the desktop macOS build — and plain
    /// <c>fsync</c> does not flush the device write cache there, so a durable
    /// acknowledgement would be unsafe under power loss. This issues
    /// <c>fcntl(F_FULLFSYNC)</c> directly on those platforms and uses the
    /// runtime flush, which is already correct, everywhere else.
    /// </summary>
    /// <param name="handle">The open file handle to flush.</param>
    /// <exception cref="IOException">The full flush failed.</exception>
    private static void FlushFull(SafeFileHandle handle)
    {
        if(OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst())
        {
            //The handle is alive for the call: it is the caller's open handle.
            int descriptor = handle.DangerousGetHandle().ToInt32();
            if(Fcntl(descriptor, FullFsync, 0) == -1)
            {
                throw new IOException($"Could not fully flush the file to stable storage (errno {Marshal.GetLastPInvokeError()}).");
            }

            return;
        }

        RandomAccess.FlushToDisk(handle);
    }

    /// <summary>Makes <paramref name="stagedPath"/> live at <paramref name="finalPath"/> by an atomic rename, then flushes the target directory through <paramref name="barrier"/> — the single commit point.</summary>
    /// <remarks>Where <paramref name="barrier"/> reaches the parent directory (the Linux and Apple defaults) the live name is on stable storage before this returns. On Windows the production barrier is a no-op — no public directory-fsync API exists — so the commit acknowledgement this return signals can precede the rename's durability: the rename is crash-consistent and eventually durable through NTFS metadata journaling, but a power loss shortly after can revert to the prior committed generation, which stays wholly intact (atomicity holds; ack-durability does not).</remarks>
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

    /// <summary>Flushes a directory's metadata to disk on the platforms that expose it: Linux and the Apple platforms (macOS, iOS, tvOS, Mac Catalyst) via a guarded <c>open</c>+<c>fsync</c>+<c>close</c>, Windows and others a no-op. On Windows the no-op means the atomic rename's directory entry is not forced to stable storage at the commit point, so the commit acknowledgement can precede the rename's durability; the rename stays crash-consistent and eventually durable through NTFS metadata journaling, and a power loss shortly after an acknowledged commit reverts to the prior committed generation, which stays wholly intact. The file bytes a publish makes live get the stronger <c>F_FULLFSYNC</c> on Apple mobile (see <see cref="FlushFull"/>); the directory metadata flush stays a best-effort <c>fsync</c> that a consumer can strengthen through the injected barrier.</summary>
    /// <param name="directoryPath">The directory whose metadata is flushed.</param>
    /// <exception cref="IOException">The directory could not be opened or flushed.</exception>
    private static void FlushDirectoryMetadata(string directoryPath)
    {
        if(!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS() && !OperatingSystem.IsMacCatalyst())
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
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("tvos")]
    [SupportedOSPlatform("maccatalyst")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint ="open", SetLastError = true)]
    private static extern int Open(byte[] pathname, int flags);

    /// <summary>Flushes a descriptor's file (or directory) to stable storage.</summary>
    /// <param name="descriptor">The file descriptor.</param>
    /// <returns>0 on success, a negative value on error.</returns>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("tvos")]
    [SupportedOSPlatform("maccatalyst")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint ="fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);

    /// <summary>Closes a file descriptor.</summary>
    /// <param name="descriptor">The file descriptor.</param>
    /// <returns>0 on success, a negative value on error.</returns>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("tvos")]
    [SupportedOSPlatform("maccatalyst")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint ="close", SetLastError = true)]
    private static extern int Close(int descriptor);

    /// <summary>Issues a control operation on a file descriptor; used for the Darwin <c>F_FULLFSYNC</c> full-flush command.</summary>
    /// <param name="descriptor">The file descriptor.</param>
    /// <param name="command">The control command.</param>
    /// <param name="argument">The command argument; ignored by <c>F_FULLFSYNC</c>.</param>
    /// <returns>A non-negative value on success, <c>-1</c> on error.</returns>
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("tvos")]
    [SupportedOSPlatform("maccatalyst")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int descriptor, int command, int argument);
}
