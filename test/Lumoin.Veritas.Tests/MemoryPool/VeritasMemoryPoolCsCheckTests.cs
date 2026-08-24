using CsCheck;
using Lumoin.Veritas.Core.Memory;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Tests.MemoryPool;

[TestClass]
internal sealed class VeritasMemoryPoolCsCheckTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void PropertyRentAlwaysReturnsExactSize()
    {
        Gen.Int[1, 10000].Sample(bufferSize =>
        {
            using VeritasMemoryPool<byte> pool = new();
            using IMemoryOwner<byte> buffer = pool.Rent(bufferSize);

            Assert.AreEqual(bufferSize, buffer.Memory.Length,
                $"Buffer size {bufferSize} should return exactly {bufferSize} elements.");
        });
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void PropertyMultipleRentReturnCycles()
    {
        Gen.Int[1, 100].Sample(cycleCount =>
        {
            using VeritasMemoryPool<byte> pool = new();

            for(int i = 0; i < cycleCount; i++)
            {
                int bufferSize = (i % 10) + 1;
                using IMemoryOwner<byte> buffer = pool.Rent(bufferSize);

                Assert.AreEqual(bufferSize, buffer.Memory.Length);

                if(buffer.Memory.Length > 0)
                {
                    buffer.Memory.Span[0] = (byte)(i % 256);
                }
            }
        });
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void PropertyConcurrentOperationsAreThreadSafe()
    {
        Gen.Int[1, 20].Sample(threadCount =>
        {
            using VeritasMemoryPool<byte> pool = new();
            Task[] tasks = new Task[threadCount];
            ConcurrentBag<Exception> exceptions = [];

            for(int i = 0; i < threadCount; i++)
            {
                int threadId = i;
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        for(int j = 0; j < 100; j++)
                        {
                            int size = (j % 50) + 1;
                            using IMemoryOwner<byte> buffer = pool.Rent(size);

                            Assert.AreEqual(size, buffer.Memory.Length);

                            if(buffer.Memory.Length > 0)
                            {
                                buffer.Memory.Span.Fill((byte)(threadId % 256));
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }, TestContext.CancellationToken);
            }

            Task.WaitAll(tasks, TestContext.CancellationToken);

            Assert.IsTrue(exceptions.IsEmpty,
                $"No exceptions should occur during concurrent operations. Found: {string.Join(", ", exceptions)}.");
        });
    }

    [TestMethod]
    public void PropertyMemoryIsInaccessibleAfterDisposal()
    {
        Gen.Int[1, 1000].Sample(bufferSize =>
        {
            using VeritasMemoryPool<byte> pool = new();

            IMemoryOwner<byte> buffer = pool.Rent(bufferSize);
            buffer.Memory.Span.Fill(0xAA);
            buffer.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = buffer.Memory,
                "Accessing disposed buffer should throw ObjectDisposedException.");
        });
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void PropertyHandlesVariousBufferSizeDistributions()
    {
        //Common RDF term sizes: short IRIs, long IRIs, blank node labels, literal values.
        int[] rdfSizes = [4, 8, 16, 32, 48, 64, 128, 256, 512, 1024];

        Gen.Int[0, rdfSizes.Length - 1].Array[1, 100].Sample(sizeIndices =>
        {
            using VeritasMemoryPool<byte> pool = new();
            List<IMemoryOwner<byte>> buffers = [];

            try
            {
                foreach(int index in sizeIndices)
                {
                    int size = rdfSizes[index];
                    IMemoryOwner<byte> buffer = pool.Rent(size);
                    buffers.Add(buffer);

                    Assert.AreEqual(size, buffer.Memory.Length);
                }
            }
            finally
            {
                foreach(IMemoryOwner<byte> buffer in buffers)
                {
                    buffer.Dispose();
                }
            }
        });
    }

    [TestMethod]
    public void PropertyTrimExcessNeverBreaksActiveRentals()
    {
        Gen.Int[1, 50].Sample(operationCount =>
        {
            using System.Diagnostics.Metrics.Meter meter = new("PropertyTest", "1.0.0");
            using VeritasMemoryPool<byte> pool = new(
                meter,
                capacityStrategy: _ => 2);

            List<IMemoryOwner<byte>> activeBuffers = [];

            try
            {
                for(int i = 0; i < operationCount; i++)
                {
                    int size = (i % 5 + 1) * 32;
                    activeBuffers.Add(pool.Rent(size));

                    //Periodically return some buffers and trim.
                    if(i % 7 == 0 && activeBuffers.Count > 1)
                    {
                        activeBuffers[0].Dispose();
                        activeBuffers.RemoveAt(0);
                        pool.TrimExcess();
                    }
                }

                //All remaining active buffers should still be accessible.
                foreach(IMemoryOwner<byte> buffer in activeBuffers)
                {
                    Assert.IsGreaterThan(0, buffer.Memory.Length,
                        "Active buffers must remain accessible after TrimExcess.");
                }
            }
            finally
            {
                foreach(IMemoryOwner<byte> buffer in activeBuffers)
                {
                    buffer.Dispose();
                }
            }
        });
    }
}
