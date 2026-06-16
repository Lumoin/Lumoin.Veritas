using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The file-backed and source-driven readers: a standalone index round-trips through a temp file read
/// memory-mapped, streamed, and Auto-selected, and across payload backings; each blob is verified per
/// source, so a byte corrupted on disk or by a composed source is detected on load; and an empty file
/// is refused. The fault injector is a test-project <see cref="ColumnarImageSource"/> subclass —
/// external code plugging into the open source seam, which is what proves the seam is extensible.
/// </summary>
[TestClass]
internal sealed class ColumnarIndexFileTests
{
    /// <summary>A few thousand distinct triples with grouped subjects/predicates/objects so the columns are multi-block.</summary>
    /// <returns>The sample triples.</returns>
    private static EncodedTriple[] SampleTriples()
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < 200; s++)
        {
            for(uint p = 0; p < 5; p++)
            {
                for(uint o = 0; o < 3; o++)
                {
                    triples.Add(EncodedTriple.FromEncoded(s, p * 10, (s * 7) + o));
                }
            }
        }

        return [.. triples];
    }

    /// <summary>Writes the index to a pool-rented, XxHash3-checksummed image and returns the bytes.</summary>
    /// <param name="index">The index to serialize.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] ToImage(ColumnarTripleIndex index)
    {
        using VeritasMemoryPool<byte> pool = new();
        int size = index.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        index.WriteTo(image, ChecksumAlgorithm.XxHash3);

        return image.ToArray();
    }

    /// <summary>Asserts the index's merged triple set equals <paramref name="expected"/>.</summary>
    /// <param name="expected">The expected triples.</param>
    /// <param name="actual">The index to check.</param>
    private static void AssertSameTriples(IEnumerable<EncodedTriple> expected, ColumnarTripleIndex actual)
    {
        HashSet<EncodedTriple> expectedSet = [.. expected];
        HashSet<EncodedTriple> actualSet = [.. actual.EnumerateTriples()];
        Assert.IsTrue(expectedSet.SetEquals(actualSet), "The triple set differs after the file round-trip.");
    }

    /// <summary>Builds a pooled image source holding a copy of <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The image bytes to copy.</param>
    /// <param name="pool">The pool the buffer is rented from.</param>
    /// <returns>The pooled image source.</returns>
    private static PooledColumnarImageSource BufferSource(byte[] bytes, MemoryPool<byte> pool)
    {
        IMemoryOwner<byte> owner = pool.Rent(bytes.Length);
        bytes.CopyTo(owner.Memory.Span);

        return new PooledColumnarImageSource(owner, bytes.Length);
    }

    /// <summary>An index round-trips through a temp file read with each access mode, warm and logically faithful.</summary>
    [TestMethod]
    public void FileRoundTripsAcrossAccessModes()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples, ColumnarOrderSetMode.ThreeRotations);
        byte[] image = ToImage(index);

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, image);
            foreach(ColumnAccessMode mode in (ColumnAccessMode[])[ColumnAccessMode.Auto, ColumnAccessMode.MemoryMapped, ColumnAccessMode.Streamed])
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
                using VeritasMemoryPool<byte> imagePool = new();
                using VeritasMemoryPool<EncodedTriple> deltaPool = new();
                ColumnarTripleIndex restored = ColumnarIndexFile.Read(handle, mode, imagePool, deltaPool);

                Assert.AreEqual(index.TripleCount, restored.TripleCount, $"The triple count differs (mode {mode}).");
                AssertSameTriples(triples, restored);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A native-backed index reloads through a memory-mapped file into native backing.</summary>
    [TestMethod]
    public void NativeBackedIndexRoundTripsThroughAMemoryMappedFile()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples, backing: ColumnPayloadBacking.NativeAligned);
        byte[] image = ToImage(index);

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, image);
            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
            using VeritasMemoryPool<byte> imagePool = new();
            using VeritasMemoryPool<EncodedTriple> deltaPool = new();
            ColumnarTripleIndex restored = ColumnarIndexFile.Read(handle, ColumnAccessMode.MemoryMapped, imagePool, deltaPool);

            Assert.AreEqual(ColumnPayloadBacking.NativeAligned, restored.Backing);
            AssertSameTriples(triples, restored);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A byte corrupted on disk fails its blob's checksum on a memory-mapped load — per-block-on-read detection on the file path.</summary>
    [TestMethod]
    public void CorruptFileIsRefusedOnMemoryMappedLoad()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        byte[] image = ToImage(index);

        //Corrupt the first byte of the first column blob (located via a verify round, since the image
        //tail is now the front-matter checksum trailer); that blob's checksum must fail on a mapped load.
        VerifyRoundReport report = ColumnarTripleIndex.RunVerifyRound(image);
        image[(int)report.Blobs[0].ByteOffset] ^= 0xFF;

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, image);
            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
            using VeritasMemoryPool<byte> imagePool = new();
            using VeritasMemoryPool<EncodedTriple> deltaPool = new();
            Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarIndexFile.Read(handle, ColumnAccessMode.MemoryMapped, imagePool, deltaPool); });
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An empty file is refused as not-an-image rather than failing inside the mapping.</summary>
    [TestMethod]
    public void EmptyFileIsRefused()
    {
        string path = Path.GetTempFileName();
        try
        {
            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
            using VeritasMemoryPool<byte> imagePool = new();
            using VeritasMemoryPool<EncodedTriple> deltaPool = new();
            Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarIndexFile.Read(handle, ColumnAccessMode.MemoryMapped, imagePool, deltaPool); });
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A clean composed source reads, and a source that corrupts a blob byte is detected on load — the corrupting source being external code on the open seam.</summary>
    [TestMethod]
    public void ComposedSourceReadsAndCorruptionIsDetected()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        byte[] image = ToImage(index);

        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();

        using(ColumnarImageSource clean = BufferSource(image, pool))
        {
            ColumnarTripleIndex restored = ColumnarIndexFile.Read(clean, deltaPool);
            AssertSameTriples(triples, restored);
        }

        //Corrupt the first byte of the first column blob (located via a verify round), so the failure
        //is a per-blob checksum on the source decode path, not the front-matter trailer at the tail.
        VerifyRoundReport report = ColumnarTripleIndex.RunVerifyRound(image);
        using(CorruptingColumnarImageSource corrupting = new(BufferSource(image, pool), pool, (int)report.Blobs[0].ByteOffset))
        {
            Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarIndexFile.Read(corrupting, deltaPool); });
        }
    }

    /// <summary>
    /// A <see cref="ColumnarImageSource"/> decorator that copies the wrapped source's image and flips
    /// one byte — external code on the open seam, owning and disposing the wrapped source.
    /// </summary>
    private sealed class CorruptingColumnarImageSource : ColumnarImageSource
    {
        /// <summary>The wrapped source, disposed with this decorator.</summary>
        private readonly ColumnarImageSource inner;

        /// <summary>The corrupted copy of the wrapped image.</summary>
        private readonly IMemoryOwner<byte> owner;

        /// <summary>The image length.</summary>
        private readonly int length;

        /// <summary>Copies the wrapped image and flips the byte at <paramref name="corruptAt"/>.</summary>
        /// <param name="inner">The source to corrupt; taken over and disposed with this decorator.</param>
        /// <param name="pool">The pool the corrupted copy is rented from.</param>
        /// <param name="corruptAt">The byte offset to flip.</param>
        public CorruptingColumnarImageSource(ColumnarImageSource inner, MemoryPool<byte> pool, int corruptAt)
        {
            this.inner = inner;
            ReadOnlySpan<byte> source = inner.Image;
            length = source.Length;
            owner = pool.Rent(length);
            Span<byte> copy = owner.Memory.Span[..length];
            source.CopyTo(copy);
            copy[corruptAt] ^= 0xFF;
        }

        /// <inheritdoc/>
        public override ReadOnlySpan<byte> Image => owner.Memory.Span[..length];

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                owner.Dispose();
                inner.Dispose();
            }
        }
    }
}
