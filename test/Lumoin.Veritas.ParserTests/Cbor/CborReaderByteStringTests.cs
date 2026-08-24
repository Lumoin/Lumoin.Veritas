using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Cbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class CborReaderByteStringTests
{
    [TestMethod]
    public void DefiniteLengthByteStringReadReturnsSliceOfSource()
    {
        //CBOR byte string of four bytes: 0x44 (major 2, ai 4) followed by payload.
        byte[] source = [0x44, 0xDE, 0xAD, 0xBE, 0xEF];
        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ReadOnlyMemory<byte> result = reader.ReadByteStringMemory();

        Assert.IsTrue(MemoryMarshal.TryGetArray(result, out ArraySegment<byte> segment));
        Assert.AreSame(source, segment.Array);
        Assert.AreEqual(1, segment.Offset);
        Assert.HasCount(4, segment);
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void DefiniteLengthByteStringSpanReadReturnsCorrectContent()
    {
        byte[] source = [0x44, 0xDE, 0xAD, 0xBE, 0xEF];
        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ReadOnlySpan<byte> span = reader.ReadByteStringSpan();

        Assert.AreEqual(4, span.Length);
        Assert.AreEqual(0xDE, span[0]);
        Assert.AreEqual(0xEF, span[3]);
    }

    [TestMethod]
    public void IndefiniteByteStringSpanReadThrows()
    {
        //Indefinite byte string with one definite chunk then break.
        byte[] payload = [0x5F, 0x42, 0x01, 0x02, 0xFF];
        CborReader reader = new(payload, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = reader.ReadByteStringSpan());
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void DefiniteByteStringPooledReadReturnsOwnedBuffer()
    {
        byte[] source = [0x44, 0xDE, 0xAD, 0xBE, 0xEF];
        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax));

        using IMemoryOwner<byte> owner = reader.ReadByteStringPooled();

        Assert.AreEqual(4, owner.Memory.Length);
        Assert.AreEqual(0xDE, owner.Memory.Span[0]);
        Assert.AreEqual(0xEF, owner.Memory.Span[3]);
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void IndefiniteByteStringPooledReadAssemblesChunks()
    {
        //Indefinite byte string with chunks "AB" and "CD" then break.
        byte[] payload = [0x5F, 0x42, 0x41, 0x42, 0x42, 0x43, 0x44, 0xFF];
        CborReader reader = new(payload, CborSerializerOptions.Default(CborConformanceMode.Lax));

        using IMemoryOwner<byte> owner = reader.ReadByteStringPooled();

        Assert.AreEqual(4, owner.Memory.Length);
        Assert.AreEqual((byte)'A', owner.Memory.Span[0]);
        Assert.AreEqual((byte)'D', owner.Memory.Span[3]);
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void DefiniteByteStringPooledReadUsesConsumerPoolAndTrimsOversizedRental()
    {
        //A four-byte byte string. The reader rents from the supplied pool and copies the bytes into the rental;
        //if an oversized buffer were not trimmed to the exact length, the copy span would be too long and would
        //overread (or fail). Trailing bytes after the byte string make that overread observable.
        byte[] source = [0x44, 0xDE, 0xAD, 0xBE, 0xEF, 0x99, 0x99, 0x99];
        using OversizedMemoryPool pool = new();
        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax), pool);

        using IMemoryOwner<byte> owner = reader.ReadByteStringPooled();

        //The consumer's own (non-Veritas) pool was used, and its oversized buffer was trimmed to exact length.
        Assert.AreEqual(1, pool.Rentals);
        Assert.AreEqual(4, owner.Memory.Length);
        Assert.AreEqual(0xDE, owner.Memory.Span[0]);
        Assert.AreEqual(0xEF, owner.Memory.Span[3]);
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void IndefiniteByteStringPooledReadWithConsumerPoolTrimsOversizedRental()
    {
        //The indefinite-length assembly path rents a single slab for the assembled bytes; an oversized rental
        //from a consumer pool must be trimmed so the owner reports the exact assembled length.
        byte[] payload = [0x5F, 0x42, 0x41, 0x42, 0x42, 0x43, 0x44, 0xFF];
        using OversizedMemoryPool pool = new();
        CborReader reader = new(payload, CborSerializerOptions.Default(CborConformanceMode.Lax), pool);

        using IMemoryOwner<byte> owner = reader.ReadByteStringPooled();

        Assert.AreEqual(1, pool.Rentals);
        Assert.AreEqual(4, owner.Memory.Length);
        Assert.AreEqual((byte)'A', owner.Memory.Span[0]);
        Assert.AreEqual((byte)'D', owner.Memory.Span[3]);
    }

    /// <summary>
    /// A consumer-style memory pool that is NOT a VeritasMemoryPool and deliberately rents buffers larger than
    /// requested. It proves the reader accepts an arbitrary <see cref="MemoryPool{T}"/> (the widened ctor seam)
    /// and trims oversized rentals to an exact-length view.
    /// </summary>
    private sealed class OversizedMemoryPool: MemoryPool<byte>
    {
        private int rentals;

        /// <summary>The number of buffers rented from this pool.</summary>
        internal int Rentals => rentals;

        /// <inheritdoc/>
        public override int MaxBufferSize => int.MaxValue;

        /// <inheritdoc/>
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            rentals++;
            int size = (minBufferSize < 0 ? 0 : minBufferSize) + 16;

            return new ArrayOwner(new byte[size]);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
        }

        /// <summary>An owner over a plain array; its <see cref="Memory"/> is the whole (oversized) array.</summary>
        private sealed class ArrayOwner: IMemoryOwner<byte>
        {
            private readonly byte[] array;

            internal ArrayOwner(byte[] array)
            {
                this.array = array;
            }

            public Memory<byte> Memory => array;

            public void Dispose()
            {
            }
        }
    }
}
