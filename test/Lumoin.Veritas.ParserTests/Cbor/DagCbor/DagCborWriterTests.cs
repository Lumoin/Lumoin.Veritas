using System;
using System.Buffers;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.DagCbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Cbor.DagCbor;

[TestClass]
internal sealed class DagCborWriterTests
{
    [TestMethod]
    public void WriteSimpleObjectProducesExpectedBytes()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteStartMap(1);
        writer.WriteTextString("k");
        writer.WriteInt32(1);
        writer.WriteEndMap();

        //Wire: A1 61 6B 01 -> map(1) {"k": 1}
        Assert.AreEqual("A1616B01", Convert.ToHexString(buffer.WrittenSpan.ToArray()));
    }

    [TestMethod]
    public void WriteRejectsNaN()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => writer.WriteDouble(double.NaN));
        Assert.AreEqual("NoNanOrInfinity", ex.RuleName);
    }

    [TestMethod]
    public void WriteRejectsPositiveInfinity()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => writer.WriteDouble(double.PositiveInfinity));
        Assert.AreEqual("NoNanOrInfinity", ex.RuleName);
    }

    [TestMethod]
    public void WriteRejectsNegativeInfinity()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => writer.WriteDouble(double.NegativeInfinity));
        Assert.AreEqual("NoNanOrInfinity", ex.RuleName);
    }

    [TestMethod]
    public void WriteFiniteDoubleEmitsBinary64()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteDouble(1.5);

        //RfcCanonical mode does not reduce 1.5 to half-precision;
        //rule 4 requires binary64 output: FB + 8 bytes.
        Assert.AreEqual(9, buffer.WrittenCount);
        Assert.AreEqual(0xFB, buffer.WrittenSpan[0]);
    }

    [TestMethod]
    public void WriteRejectsIntegerInMapKeyPosition()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteStartMap(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.WriteInt32(1));
    }

    [TestMethod]
    public void WriteRejectsByteStringInMapKeyPosition()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteStartMap(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.WriteByteString([0x00]));
    }

    [TestMethod]
    public void WriteMapWithStringKeyEmitsSortedOrder()
    {
        //Under RfcCanonical mode the inner CborWriter sorts map entries
        //by length-first lexical key order per RFC 7049 §3.9, satisfying
        //DAG-CBOR rule 2's map-key ordering requirement.
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteStartMap(3);
        writer.WriteTextString("bb");
        writer.WriteInt32(2);
        writer.WriteTextString("a");
        writer.WriteInt32(1);
        writer.WriteTextString("ccc");
        writer.WriteInt32(3);
        writer.WriteEndMap();

        //Expected order after RFC 7049 §3.9 sort: "a" (1 byte), "bb" (2), "ccc" (3).
        //A3 61 61 01 62 62 62 02 63 63 63 63 03
        Assert.AreEqual("A3616101626262026363636303", Convert.ToHexString(buffer.WrittenSpan.ToArray()));
    }
}
