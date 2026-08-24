using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The windowed segment-image-source contract: a memory-mapped source addresses a file past a single
/// span's range through bounded <see cref="SegmentImageSource.Slice"/> windows at long offsets — the
/// first genuine cross-boundary read pin, over a sparse file so the multi-gigabyte image costs almost
/// no disk — while the whole-image accessor throws loudly past the span range instead of truncating;
/// a pooled source serves windows byte-identical to its whole image; and an incremental checksum
/// session over windowed appends produces exactly the one-shot digest, the law the streamed manifest
/// verification rests on.
/// </summary>
[TestClass]
internal sealed partial class WindowedSegmentImageSourceTests
{
    /// <summary>A file length safely past the span range, so a whole-image span is impossible by construction.</summary>
    private const long BeyondSpanLength = 2_500_000_000;

    /// <summary>The filesystem control code that marks a file sparse, so the unwritten gaps allocate no clusters.</summary>
    private const uint FsctlSetSparse = 0x000900C4;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The governed pool the pooled-source and digest pins rent from.</summary>
    private static VeritasMemoryPool<byte> Pool { get; } = new();

    /// <summary>A mapped source over a file past the span range serves correct windows below, straddling, and above the two-gigabyte boundary, reports the full long length, and reads the unwritten gaps as zeros.</summary>
    [TestMethod]
    public void MappedSourceSlicesAcrossTheSpanBoundary()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-2gib-").FullName;
        string path = Path.Combine(directory, "beyond-span.bin");
        try
        {
            WriteSparseSentinelFile(path);

            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using MemoryMappedSegmentImageSource source = MemoryMappedSegmentImageSource.Open(handle, BeyondSpanLength);

            Assert.AreEqual(BeyondSpanLength, source.Length, "The source reports the full long length.");
            Assert.AreSequenceEqual(Sentinel(1), source.Slice(1_000, 4).ToArray(), "The window below the boundary reads its sentinel.");
            Assert.AreSequenceEqual(Sentinel(2), source.Slice(int.MaxValue - 2L, 4).ToArray(), "The window straddling the two-gigabyte boundary reads its sentinel whole.");
            Assert.AreSequenceEqual(Sentinel(3), source.Slice(2_400_000_000, 4).ToArray(), "The window above the boundary reads its sentinel.");
            Assert.AreSequenceEqual(Sentinel(4), source.Slice(BeyondSpanLength - 4, 4).ToArray(), "The window at the image's end reads its sentinel.");
            Assert.AreEqual(-1, source.Slice(1_000_000_000, 8).IndexOfAnyExcept((byte)0), "An unwritten gap reads as zeros.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The whole-image accessor throws loudly for an image past the span range — a reader that has not adopted windows fails rather than decoding a truncated prefix.</summary>
    [TestMethod]
    public void WholeImageAccessorThrowsPastTheSpanRange()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-2gib-").FullName;
        string path = Path.Combine(directory, "beyond-span.bin");
        try
        {
            WriteSparseSentinelFile(path);

            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using MemoryMappedSegmentImageSource source = MemoryMappedSegmentImageSource.Open(handle, BeyondSpanLength);

            Assert.ThrowsExactly<InvalidOperationException>(() => _ = source.Image, "The whole-image accessor must refuse an image past a span's range, never truncate it.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A pooled source's windows are byte-identical to its whole image, its bounds are enforced, and a disposed source refuses to serve.</summary>
    [TestMethod]
    public void PooledSourceWindowsMatchTheWholeImage()
    {
        const int length = 4_096;
        IMemoryOwner<byte> owner = Pool.Rent(length);
        FillDeterministic(owner.Memory.Span[..length], seed: 0x51D3_0001UL);
        byte[] expected = owner.Memory.Span[..length].ToArray();

        PooledSegmentImageSource source = new(owner, length);
        try
        {
            Assert.AreEqual(length, source.Length);
            Assert.AreSequenceEqual(expected, source.Image.ToArray(), "The whole image serves through the window funnel.");
            Assert.AreSequenceEqual(expected[100..228], source.Slice(100, 128).ToArray(), "A sub-window matches the same bytes of the whole image.");
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = source.Slice(length - 4, 8), "A window past the image end is refused.");
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = source.Slice(-1, 4), "A negative offset is refused.");
        }
        finally
        {
            source.Dispose();
        }

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = source.Slice(0, 16), "A disposed source refuses to serve windows.");
    }

    /// <summary>An incremental checksum session fed windowed appends finishes with exactly the one-shot digest, for both built-in algorithms and across uneven window sizes — the law the streamed manifest verification rests on.</summary>
    [TestMethod]
    public void ChecksumSessionMatchesOneShotCompute()
    {
        const int length = 8 * 1024 * 1024;
        IMemoryOwner<byte> owner = Pool.Rent(length);
        try
        {
            Span<byte> data = owner.Memory.Span[..length];
            FillDeterministic(data, seed: 0x51D3_0002UL);

            AssertSessionMatchesOneShot(ChecksumAlgorithm.XxHash3, data);
            AssertSessionMatchesOneShot(ChecksumAlgorithm.Crc32, data);
        }
        finally
        {
            owner.Dispose();
        }
    }

    /// <summary>Digests <paramref name="data"/> one-shot and through a session over deliberately uneven windows, asserting byte-identical results.</summary>
    /// <param name="algorithm">The algorithm under test; must carry a session factory.</param>
    /// <param name="data">The bytes to digest.</param>
    private static void AssertSessionMatchesOneShot(ChecksumAlgorithm algorithm, ReadOnlySpan<byte> data)
    {
        Assert.IsNotNull(algorithm.CreateSession, $"{algorithm.Name} must carry the streaming capability.");

        Span<byte> oneShot = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        algorithm.Compute(data, oneShot[..algorithm.ByteWidth]);

        Span<byte> streamed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        using(ChecksumSession session = algorithm.CreateSession())
        {
            //Uneven windows, including a one-byte window, so any internal blocking seam is crossed unaligned.
            int offset = 0;
            int window = 1;
            while(offset < data.Length)
            {
                int take = Math.Min(window, data.Length - offset);
                session.Append(data.Slice(offset, take));
                offset += take;
                window = (window * 3) + 1;
            }

            session.Finish(streamed[..algorithm.ByteWidth]);
        }

        Assert.AreSequenceEqual(oneShot[..algorithm.ByteWidth].ToArray(), streamed[..algorithm.ByteWidth].ToArray(), $"{algorithm.Name}: the windowed session must finish with the one-shot digest.");
    }

    /// <summary>Creates the beyond-span sentinel file: marked sparse where the filesystem supports it (the unwritten gaps then allocate no clusters; elsewhere the file simply allocates whole), with four four-byte sentinels below, straddling, above, and at the end of the span boundary.</summary>
    /// <param name="path">The file path to create.</param>
    private static void WriteSparseSentinelFile(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        TryMarkSparse(handle);

        RandomAccess.Write(handle, Sentinel(1), 1_000);
        RandomAccess.Write(handle, Sentinel(2), int.MaxValue - 2L);
        RandomAccess.Write(handle, Sentinel(3), 2_400_000_000);
        RandomAccess.Write(handle, Sentinel(4), BeyondSpanLength - 4);
    }

    /// <summary>Builds the four-byte sentinel pattern for a marker index.</summary>
    /// <param name="marker">The marker index.</param>
    /// <returns>The sentinel bytes.</returns>
    private static byte[] Sentinel(byte marker)
    {
        return [marker, 0xBE, 0xEF, marker];
    }

    /// <summary>Fills a span with deterministic splitmix64 bytes.</summary>
    /// <param name="destination">The span to fill.</param>
    /// <param name="seed">The stream seed.</param>
    private static void FillDeterministic(Span<byte> destination, ulong seed)
    {
        ulong state = seed;
        int offset = 0;
        Span<byte> draw = stackalloc byte[sizeof(ulong)];
        while(offset < destination.Length)
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            ulong z = state;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            z ^= z >> 31;
            BinaryPrimitives.WriteUInt64LittleEndian(draw, z);
            int take = Math.Min(sizeof(ulong), destination.Length - offset);
            draw[..take].CopyTo(destination[offset..]);
            offset += take;
        }
    }

    /// <summary>Marks the file sparse where the platform and filesystem support it; a failure is deliberately ignored — the pin stays correct on a fully-allocated file, only slower and larger.</summary>
    /// <param name="handle">The open writable file handle.</param>
    private static void TryMarkSparse(SafeFileHandle handle)
    {
        if(!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = DeviceIoControl(handle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
    }

    /// <summary>The filesystem control call that sets the sparse attribute.</summary>
    /// <param name="device">The open file handle.</param>
    /// <param name="ioControlCode">The control code.</param>
    /// <param name="inBuffer">Unused input buffer.</param>
    /// <param name="inBufferSize">Unused input size.</param>
    /// <param name="outBuffer">Unused output buffer.</param>
    /// <param name="outBufferSize">Unused output size.</param>
    /// <param name="bytesReturned">Receives the returned byte count.</param>
    /// <param name="overlapped">Unused overlapped pointer.</param>
    /// <returns>Whether the call succeeded.</returns>
    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
}
