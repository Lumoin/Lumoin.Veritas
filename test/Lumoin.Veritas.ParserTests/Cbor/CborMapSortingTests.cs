using System;
using System.Buffers;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Tests that the writer emits map keys in the order the active conformance
/// mode requires. Lax/Strict modes preserve insertion order; the canonical
/// modes sort by encoded keys with their respective rules (RFC canonical
/// and CTAP2 sort length-first then bytewise; CDE sorts bytewise only).
/// </summary>
[TestClass]
internal sealed class CborMapSortingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void LaxModePreservesInsertionOrder()
    {
        byte[] bytes = WriteSampleMap(CborConformanceMode.Lax);
        //Two-pair map {2: 20, 1: 10} in insertion order. All four payloads are immediate.
        Assert.AreSequenceEqual(new byte[] { 0xA2, 0x02, 0x14, 0x01, 0x0A }, bytes);
    }

    [TestMethod]
    public void RfcCanonicalSortsIntegerKeysAscending()
    {
        byte[] bytes = WriteSampleMap(CborConformanceMode.RfcCanonical);
        //Same two-pair map; canonical mode sorts so 1 precedes 2.
        Assert.AreSequenceEqual(new byte[] { 0xA2, 0x01, 0x0A, 0x02, 0x14 }, bytes);
    }

    [TestMethod]
    public void RfcCanonicalLengthFirstThenBytewise()
    {
        //Keys: "a" (1 byte body, header 0x61 + 0x61 = 2 total), "bb" (3 total), 10 (1 total), 100 (2 total)
        //Encoded keys (with header):
        //  "a": 0x61 0x61            (2 bytes)
        //  "bb": 0x62 0x62 0x62      (3 bytes)
        //  10: 0x0A                  (1 byte)
        //  100: 0x18 0x64            (2 bytes)
        //RFC canonical length-first:
        //  Length 1: 10 (0x0A)
        //  Length 2: 100 (0x18 0x64), "a" (0x61 0x61) — sorted bytewise: 100 first
        //  Length 3: "bb"
        //Expected order: 10, 100, "a", "bb"
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.RfcCanonical));
        writer.WriteStartMap(4);
        writer.WriteTextString("bb");
        writer.WriteUInt64(2);
        writer.WriteUInt64(100);
        writer.WriteUInt64(3);
        writer.WriteTextString("a");
        writer.WriteUInt64(4);
        writer.WriteUInt64(10);
        writer.WriteUInt64(1);
        writer.WriteEndMap();

        byte[] bytes = buffer.WrittenSpan.ToArray();
        //Expected: a4 0a 01 18 64 03 61 61 04 62 62 62 02
        //          map(4) | key=10 val=1 | key=100 val=3 | key="a" val=4 | key="bb" val=2
        Assert.AreSequenceEqual(
            new byte[] { 0xA4, 0x0A, 0x01, 0x18, 0x64, 0x03, 0x61, 0x61, 0x04, 0x62, 0x62, 0x62, 0x02 },
            bytes);
    }

    [TestMethod]
    public void CdeSortsBytewiseWithoutLengthFirst()
    {
        //Same keys: "a", "bb", 10, 100
        //CDE sorts purely bytewise:
        //  10 (0x0A) — first byte 0x0A
        //  100 (0x18 0x64) — first byte 0x18
        //  "a" (0x61 0x61) — first byte 0x61
        //  "bb" (0x62 0x62 0x62) — first byte 0x62
        //So bytewise order: 10, 100, "a", "bb" — same as RFC canonical here because the
        //length-first effect doesn't reorder this particular set. Use a more
        //discriminating set: "ab" (0x62 0x61 0x62), "z" (0x61 0x7A).
        //  Lengths: "z" = 2, "ab" = 3.
        //  RFC canonical: length 2 first → "z", "ab".
        //  CDE bytewise:  0x61 < 0x62 → "z", "ab".  Same order.
        //Choose: "z" (0x61 0x7A) vs 100 (0x18 0x64). Lengths both 2.
        //  Bytewise: 0x18 < 0x61 → 100, "z" in both modes. Still same.
        //Use lengths that actually differ: keys "aa" (3 bytes encoded, 0x62 0x61 0x61) vs 0 (1 byte, 0x00).
        //  RFC canonical: length 1 first → 0, "aa".
        //  CDE bytewise: 0x00 < 0x62 → 0, "aa". Still same.
        //To get different orderings, find a long short-prefix key vs a longer key with a smaller first byte.
        //Example: "ab" (3 bytes total, 0x62 0x61 0x62) vs "abc" (4 bytes total, 0x63 0x61 0x62 0x63).
        //  Lengths: 3 vs 4. RFC canonical → "ab" first. CDE: 0x62 < 0x63 → "ab" first. Same.
        //Hard: short vs long with big first byte.
        //  Keys: "z" (2 bytes, 0x61 0x7A) and "aaa" (4 bytes, 0x63 0x61 0x61 0x61).
        //  RFC canonical length-first: 2 < 4 → "z" first.
        //  CDE bytewise: 0x61 == 0x63? No, 0x61 < 0x63 → "z" first. Same.
        //The orderings only diverge when the longer key has a SMALLER lexicographic-byte prefix.
        //  Keys: 100 (0x18 0x64) [length 2] and "" (0x60) [length 1].
        //  RFC canonical: length 1 → "", then length 2 → 100.
        //  CDE bytewise: 0x18 < 0x60 → 100 first, "" second.
        //That diverges! Use this pair.
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Cde));
        writer.WriteStartMap(2);
        writer.WriteTextString(string.Empty);
        writer.WriteUInt64(1);
        writer.WriteUInt64(100);
        writer.WriteUInt64(2);
        writer.WriteEndMap();

        byte[] bytes = buffer.WrittenSpan.ToArray();
        //Expected CDE order: 100 first (0x18 0x64), then "" (0x60).
        //a2 18 64 02 60 01
        Assert.AreSequenceEqual(new byte[] { 0xA2, 0x18, 0x64, 0x02, 0x60, 0x01 }, bytes);
    }

    [TestMethod]
    public void RfcCanonicalSortsLengthFirstOnSameDivergentKeys()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.RfcCanonical));
        writer.WriteStartMap(2);
        writer.WriteTextString(string.Empty);
        writer.WriteUInt64(1);
        writer.WriteUInt64(100);
        writer.WriteUInt64(2);
        writer.WriteEndMap();

        byte[] bytes = buffer.WrittenSpan.ToArray();
        //Expected RFC canonical order: "" first (1 byte 0x60), then 100 (2 bytes 0x18 0x64).
        //a2 60 01 18 64 02
        Assert.AreSequenceEqual(new byte[] { 0xA2, 0x60, 0x01, 0x18, 0x64, 0x02 }, bytes);
    }

    [TestMethod]
    public void NestedSortMapsSortIndependently()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.RfcCanonical));
        writer.WriteStartMap(1);
        writer.WriteTextString("outer");
        writer.WriteStartMap(2);
        writer.WriteUInt64(2);
        writer.WriteUInt64(20);
        writer.WriteUInt64(1);
        writer.WriteUInt64(10);
        writer.WriteEndMap();
        writer.WriteEndMap();

        byte[] bytes = buffer.WrittenSpan.ToArray();
        //Outer: a1 65 6f 75 74 65 72 ... (one pair, key "outer", value = inner map)
        //Inner under canonical sort: a2 01 0a 02 14 (keys 1 then 2)
        Assert.AreSequenceEqual(
            new byte[] { 0xA1, 0x65, 0x6F, 0x75, 0x74, 0x65, 0x72, 0xA2, 0x01, 0x0A, 0x02, 0x14 },
            bytes);
    }

    [TestMethod]
    public void DefiniteCountMismatchInSortModeThrowsOnEnd()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Cde));
        writer.WriteStartMap(3);
        writer.WriteUInt64(1);
        writer.WriteUInt64(10);

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndMap());
    }

    [TestMethod]
    public void HalfWrittenPairInSortModeThrowsOnEnd()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Cde));
        writer.WriteStartMap(1);
        writer.WriteUInt64(1);

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndMap());
    }

    private static byte[] WriteSampleMap(CborConformanceMode mode)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(mode));
        writer.WriteStartMap(2);
        writer.WriteUInt64(2);
        writer.WriteUInt64(20);
        writer.WriteUInt64(1);
        writer.WriteUInt64(10);
        writer.WriteEndMap();
        return buffer.WrittenSpan.ToArray();
    }
}
