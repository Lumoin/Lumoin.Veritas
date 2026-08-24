using System;
using System.Buffers;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.DagCbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Cbor.DagCbor;

[TestClass]
internal sealed class DagCborReaderStrictTests
{
    [TestMethod]
    public void StrictRejectsHalfPrecisionFloat()
    {
        //0xF9 0x3E 0x00 -> half-precision 1.5
        byte[] bytes = [0xF9, 0x3E, 0x00];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.PeekState());
        Assert.AreEqual("FloatsAlways64Bit", ex.RuleName);
    }

    [TestMethod]
    public void StrictRejectsSinglePrecisionFloat()
    {
        //0xFA + 4 bytes
        byte[] bytes = [0xFA, 0x3F, 0xC0, 0x00, 0x00];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.PeekState());
        Assert.AreEqual("FloatsAlways64Bit", ex.RuleName);
    }

    [TestMethod]
    public void StrictRejectsUndefined()
    {
        //0xF7 -> undefined
        byte[] bytes = [0xF7];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.PeekState());
        Assert.AreEqual("AllowedSimpleValues", ex.RuleName);
    }

    [TestMethod]
    public void StrictRejectsNonStandardSimpleValue()
    {
        //0xF0 -> simple value 16 (not true/false/null)
        byte[] bytes = [0xF0];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.PeekState());
        Assert.AreEqual("AllowedSimpleValues", ex.RuleName);
    }

    [TestMethod]
    public void StrictRejectsIndefiniteArray()
    {
        //0x9F + 0x01 + 0xFF -> indefinite array of [1]
        byte[] bytes = [0x9F, 0x01, 0xFF];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.ReadStartArray());
        Assert.AreEqual("DeterministicEncoding", ex.RuleName);
    }

    [TestMethod]
    public void StrictRejectsIndefiniteMap()
    {
        //0xBF + 0xFF -> indefinite empty map
        byte[] bytes = [0xBF, 0xFF];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.ReadStartMap());
        Assert.AreEqual("DeterministicEncoding", ex.RuleName);
    }

    [TestMethod]
    public void StrictRejectsIntegerKeyInMap()
    {
        //Map(1) { 1: 2 } -> A1 01 02
        byte[] bytes = [0xA1, 0x01, 0x02];
        DagCborReader reader = new(bytes, strict: true);
        reader.ReadStartMap();

        Assert.ThrowsExactly<DagCborConformanceException>(() => reader.ReadInt64());
    }

    [TestMethod]
    public void StrictReadsBinary64NaNAsError()
    {
        //FB 7FF8000000000000 -> NaN as binary64
        byte[] bytes = [0xFB, 0x7F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.ReadDouble());
        Assert.AreEqual("NoNanOrInfinity", ex.RuleName);
    }

    [TestMethod]
    public void StrictReadsBinary64InfinityAsError()
    {
        //FB 7FF0000000000000 -> +Infinity as binary64
        byte[] bytes = [0xFB, 0x7F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        DagCborReader reader = new(bytes, strict: true);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.ReadDouble());
        Assert.AreEqual("NoNanOrInfinity", ex.RuleName);
    }
}

[TestClass]
internal sealed class DagCborReaderRelaxedTests
{
    [TestMethod]
    public void RelaxedAcceptsHalfPrecisionFloat()
    {
        //0xF9 0x3E 0x00 -> half-precision 1.5
        byte[] bytes = [0xF9, 0x3E, 0x00];
        DagCborReader reader = new(bytes, strict: false);

        double value = reader.ReadDouble();
        Assert.AreEqual(1.5, value);
    }

    [TestMethod]
    public void RelaxedAcceptsSinglePrecisionFloat()
    {
        //0xFA 3FC00000 -> single-precision 1.5
        byte[] bytes = [0xFA, 0x3F, 0xC0, 0x00, 0x00];
        DagCborReader reader = new(bytes, strict: false);

        double value = reader.ReadDouble();
        Assert.AreEqual(1.5, value);
    }

    [TestMethod]
    public void RelaxedStillRejectsUndefined()
    {
        byte[] bytes = [0xF7];
        DagCborReader reader = new(bytes, strict: false);

        Assert.ThrowsExactly<DagCborConformanceException>(() => reader.PeekState());
    }

    [TestMethod]
    public void RelaxedStillRejectsIndefiniteArray()
    {
        byte[] bytes = [0x9F, 0x01, 0xFF];
        DagCborReader reader = new(bytes, strict: false);

        Assert.ThrowsExactly<DagCborConformanceException>(() => reader.ReadStartArray());
    }

    [TestMethod]
    public void RelaxedStillRejectsNaN()
    {
        byte[] bytes = [0xFB, 0x7F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        DagCborReader reader = new(bytes, strict: false);

        DagCborConformanceException ex = Assert.ThrowsExactly<DagCborConformanceException>(
            () => reader.ReadDouble());
        Assert.AreEqual("NoNanOrInfinity", ex.RuleName);
    }
}

[TestClass]
internal sealed class DagCborRoundTripTests
{
    [TestMethod]
    public void SimpleMapRoundTrip()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteStartMap(2);
        writer.WriteTextString("a");
        writer.WriteInt32(1);
        writer.WriteTextString("b");
        writer.WriteTextString("hello");
        writer.WriteEndMap();

        DagCborReader reader = new(buffer.WrittenMemory, strict: true);
        int count = reader.ReadStartMap();
        Assert.AreEqual(2, count);
        Assert.AreEqual("a", reader.ReadTextString());
        Assert.AreEqual(1L, reader.ReadInt64());
        Assert.AreEqual("b", reader.ReadTextString());
        Assert.AreEqual("hello", reader.ReadTextString());
        reader.ReadEndMap();
    }

    [TestMethod]
    public void NestedStructureRoundTrip()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteStartArray(2);
        writer.WriteStartMap(1);
        writer.WriteTextString("k");
        writer.WriteBoolean(true);
        writer.WriteEndMap();
        writer.WriteNull();
        writer.WriteEndArray();

        DagCborReader reader = new(buffer.WrittenMemory, strict: true);
        Assert.AreEqual(2, reader.ReadStartArray());
        Assert.AreEqual(1, reader.ReadStartMap());
        Assert.AreEqual("k", reader.ReadTextString());
        Assert.IsTrue(reader.ReadBoolean());
        reader.ReadEndMap();
        reader.ReadNull();
        reader.ReadEndArray();
    }

    [TestMethod]
    public void DoubleRoundTripFinitePreserved()
    {
        ArrayBufferWriter<byte> buffer = new();
        DagCborWriter writer = new(buffer);
        writer.WriteDouble(1.5);

        DagCborReader reader = new(buffer.WrittenMemory, strict: true);
        Assert.AreEqual(1.5, reader.ReadDouble());
    }
}
