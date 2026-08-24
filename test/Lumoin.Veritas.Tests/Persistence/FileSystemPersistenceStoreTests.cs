using System;
using System.IO;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The <see cref="FileSystemPersistenceStore"/> image-open seam: <see cref="PersistenceStore.OpenImage"/> is the
/// memory-efficient bulk-segment read the dictionary, system-of-record, named-graph, and columnar-sidecar loads
/// go through instead of a transient whole-image heap copy. A published artifact opens to an image byte-identical
/// to its control-plane <see cref="PersistenceStore.Read"/>; a missing or empty artifact opens to <see langword="null"/>
/// (which the load path reads as a missing-or-rejected segment); and disposing a source releases the file handle it
/// owns, so the artifact re-maps and the directory is removable afterwards.
/// </summary>
[TestClass]
internal sealed class FileSystemPersistenceStoreTests
{
    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Stages then atomically publishes <paramref name="content"/> under <paramref name="name"/> so the store holds it live.</summary>
    /// <param name="store">The store to publish into.</param>
    /// <param name="name">The live artifact name.</param>
    /// <param name="content">The artifact bytes.</param>
    private static void Publish(PersistenceStore store, string name, ReadOnlySpan<byte> content)
    {
        store.WriteStaged(name + ".staged", content);
        store.Publish(name + ".staged", name);
    }

    /// <summary>
    /// Every staged write flushes its bytes through the injected file-content
    /// flush seam, so the sound production default — <c>fcntl(F_FULLFSYNC)</c> on
    /// the Apple mobile platforms — is reached on every artifact rather than
    /// silently bypassed.
    /// </summary>
    [TestMethod]
    public void EveryStagedWriteFlushesThroughTheFlushSeam()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-store-").FullName;
        try
        {
            RecordingFlush flush = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier, flush.Flush);

            store.WriteStaged("a.staged", [1, 2, 3]);
            store.WriteStaged("b.staged", [4, 5, 6, 7]);

            Assert.AreEqual(2, flush.CallCount, "A staged write did not go through the file-content flush seam.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A test file-content durability flush that records how often it was invoked and still flushes durably through the production default, so a test can assert every staged write reaches the flush seam.</summary>
    private sealed class RecordingFlush
    {
        /// <summary>The number of times the flush was invoked.</summary>
        public int CallCount { get; private set; }

        /// <summary>Records one flush invocation and performs the production durable flush; matches the <see cref="DurableFlushDelegate"/> shape.</summary>
        /// <param name="handle">The open handle to the written file.</param>
        public void Flush(SafeFileHandle handle)
        {
            CallCount++;
            AtomicPublish.DefaultFlush(handle);
        }
    }

    /// <summary>An opened image carries exactly the bytes the artifact was published with — the same bytes the control-plane read returns — and, once the source is disposed, the artifact opens again, proving the owned file handle was released rather than left mapping the file.</summary>
    [TestMethod]
    public void OpenImageReturnsThePublishedBytesAndReleasesTheHandle()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-store-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            byte[] content = [0x56, 0x54, 0x53, 0x00, 0x01, 0x02, 0x03, 0xFF];
            Publish(store, "segment.bin", content);

            using(SegmentImageSource? source = store.OpenImage("segment.bin"))
            {
                Assert.IsNotNull(source, "A published artifact opens to an image source.");
                Assert.IsTrue(content.AsSpan().SequenceEqual(source.Image), "The opened image is not byte-identical to the published content.");
                Assert.IsTrue(store.Read("segment.bin").AsSpan().SequenceEqual(source.Image), "The opened image diverges from the control-plane read of the same artifact.");
            }

            //Re-opening after disposal proves the source released the handle it owned: a still-mapping handle would
            //keep the file open and a second map would contend with it rather than succeed cleanly.
            using SegmentImageSource? reopened = store.OpenImage("segment.bin");
            Assert.IsNotNull(reopened, "The artifact does not re-open after the first source was disposed (a leaked handle).");
            Assert.IsTrue(content.AsSpan().SequenceEqual(reopened.Image), "The re-opened image is not byte-identical to the published content.");
        }
        finally
        {
            //A leaked memory-mapping handle would make this recursive delete throw on Windows; its success is itself
            //a check that no source left the file mapped.
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Opening an artifact the store does not hold yields <see langword="null"/>, the same absent signal as the control-plane read.</summary>
    [TestMethod]
    public void OpenImageOfMissingArtifactIsNull()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-store-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            using SegmentImageSource? source = store.OpenImage("absent.bin");

            Assert.IsNull(source, "A missing artifact must open to null, not a source over no bytes.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A zero-length artifact is not a mappable segment image, so it opens to <see langword="null"/> — the load path then rejects the generation exactly as the prior whole-image read did on a length mismatch.</summary>
    [TestMethod]
    public void OpenImageOfEmptyArtifactIsNull()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-store-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Publish(store, "empty.bin", ReadOnlySpan<byte>.Empty);

            using SegmentImageSource? source = store.OpenImage("empty.bin");

            Assert.IsNull(source, "A zero-length artifact must open to null rather than a zero-length mapping.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A published artifact opens to a pooled image whose bytes — read as both span and retainable memory — match the published content and the control-plane read; once the source is disposed the buffer returns to its pool and the artifact re-opens (the pooled read closed its handle).</summary>
    [TestMethod]
    public void OpenPooledImageReturnsThePublishedBytes()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-store-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            byte[] content = [0x56, 0x54, 0x53, 0x00, 0x10, 0x20, 0x30, 0x7F, 0xFF];
            Publish(store, "pooled.bin", content);

            using(PooledSegmentImageSource? source = store.OpenPooledImage("pooled.bin", pool))
            {
                Assert.IsNotNull(source, "A published artifact opens to a pooled image source.");
                Assert.IsTrue(content.AsSpan().SequenceEqual(source.Image), "The pooled image span is not byte-identical to the published content.");
                Assert.IsTrue(content.AsSpan().SequenceEqual(source.ImageMemory.Span), "The retainable image memory is not byte-identical to the published content.");
                Assert.IsTrue(store.Read("pooled.bin").AsSpan().SequenceEqual(source.ImageMemory.Span), "The pooled image diverges from the control-plane read of the same artifact.");
            }

            //The pooled read closes its file handle before returning (the buffer is independent), so the artifact is
            //immediately removable; the recursive cleanup in the finally would otherwise throw on a held handle.
            using PooledSegmentImageSource? reopened = store.OpenPooledImage("pooled.bin", pool);
            Assert.IsNotNull(reopened, "The artifact re-opens after the first pooled source was disposed.");
            Assert.IsTrue(content.AsSpan().SequenceEqual(reopened.ImageMemory.Span), "The re-opened pooled image is not byte-identical to the published content.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Opening a missing artifact pooled yields null, the same absent signal as the control-plane read.</summary>
    [TestMethod]
    public void OpenPooledImageOfMissingArtifactIsNull()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-store-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            using PooledSegmentImageSource? source = store.OpenPooledImage("absent.bin", pool);

            Assert.IsNull(source, "A missing artifact must open to null, not a source over no bytes.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A zero-length artifact is not a loadable segment image, so the pooled open yields null — the load path then refuses the generation, exactly as the prior whole-image read's length mismatch did.</summary>
    [TestMethod]
    public void OpenPooledImageOfEmptyArtifactIsNull()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-store-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Publish(store, "empty.bin", ReadOnlySpan<byte>.Empty);

            using PooledSegmentImageSource? source = store.OpenPooledImage("empty.bin", pool);

            Assert.IsNull(source, "A zero-length artifact must open to null rather than a zero-length pooled buffer.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
