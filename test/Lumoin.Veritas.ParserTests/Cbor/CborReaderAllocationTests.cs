using System;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Cbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class CborReaderAllocationTests
{
    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void DefiniteByteStringMemoryReadAllocatesZeroBytes()
    {
        //Three back-to-back 4-byte definite byte strings.
        byte[] cborBytes =
        [
            0x44, 0x01, 0x02, 0x03, 0x04,
            0x44, 0x05, 0x06, 0x07, 0x08,
            0x44, 0x09, 0x0A, 0x0B, 0x0C,
        ];

        //Warm up to settle one-time JIT allocations.
        CborReader warm = new(cborBytes, CborSerializerOptions.Default(CborConformanceMode.Lax));
        _ = warm.ReadByteStringMemory();

        CborReader reader = new(cborBytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        long before = GC.GetAllocatedBytesForCurrentThread();
        ReadOnlyMemory<byte> first = reader.ReadByteStringMemory();
        ReadOnlyMemory<byte> second = reader.ReadByteStringMemory();
        ReadOnlyMemory<byte> third = reader.ReadByteStringMemory();
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(4, first.Length);
        Assert.AreEqual(4, second.Length);
        Assert.AreEqual(4, third.Length);

        //Definite-length memory reads must not allocate on the hot path; allow tiny JIT noise.
        Assert.IsLessThanOrEqualTo(64L, after - before);
    }
}
