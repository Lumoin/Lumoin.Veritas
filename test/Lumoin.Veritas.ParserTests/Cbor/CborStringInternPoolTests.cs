using System;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Verifies the intern-pool fast path on <see cref="CborReader.ReadTextString"/>:
/// repeated reads of the same UTF-8 bytes return the same
/// <see cref="string"/> instance, and the pool participates correctly
/// across both single-segment and cross-segment inputs. Also exercises
/// <see cref="CborReader.Reset(System.ReadOnlyMemory{byte})"/>.
/// </summary>
[TestClass]
internal sealed class CborStringInternPoolTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RepeatedKeyReadReturnsSameInstance()
    {
        //{ "foo": 1, "foo": 2 } — duplicate key for instance-equality check.
        //6463666f6f01 = key "foo" + value 1
        ReadOnlyMemory<byte> buf = Convert.FromHexString("A263666F6F0163666F6F02");
        CborStringInternPool pool = new();
        CborReader reader = new(buf, CborSerializerOptions.Default(CborConformanceMode.Lax), stringInternPool: pool);

        Assert.AreEqual(2, reader.ReadStartMap());
        string k1 = reader.ReadTextString();
        _ = reader.ReadUInt64();
        string k2 = reader.ReadTextString();
        _ = reader.ReadUInt64();
        reader.ReadEndMap();

        Assert.AreSame(k1, k2);
        Assert.AreEqual("foo", k1);
        Assert.AreEqual(1, pool.Count);
    }

    [TestMethod]
    public void PreseededKeysReturnPreseededInstance()
    {
        //{ "type": "post" } — preseed "type" so the read returns the
        //already-cached instance, not a freshly-decoded duplicate.
        ReadOnlyMemory<byte> buf = Convert.FromHexString("A1647479706564706F7374");
        CborStringInternPool pool = new();
        string canonicalType = pool.Preseed("type");
        CborReader reader = new(buf, CborSerializerOptions.Default(CborConformanceMode.Lax), stringInternPool: pool);

        Assert.AreEqual(1, reader.ReadStartMap());
        string key = reader.ReadTextString();
        _ = reader.ReadTextString();
        reader.ReadEndMap();

        Assert.AreSame(canonicalType, key);
    }

    [TestMethod]
    public void DistinctKeysCacheIndependently()
    {
        //{ "a": 1, "b": 2 }
        ReadOnlyMemory<byte> buf = Convert.FromHexString("A2616101616202");
        CborStringInternPool pool = new();
        CborReader reader = new(buf, CborSerializerOptions.Default(CborConformanceMode.Lax), stringInternPool: pool);

        reader.ReadStartMap();
        string a = reader.ReadTextString();
        _ = reader.ReadUInt64();
        string b = reader.ReadTextString();
        _ = reader.ReadUInt64();
        reader.ReadEndMap();

        Assert.AreEqual("a", a);
        Assert.AreEqual("b", b);
        Assert.AreNotSame(a, b);
        Assert.AreEqual(2, pool.Count);
    }

    [TestMethod]
    public void ResetClearsStackAndPosition()
    {
        ReadOnlyMemory<byte> first = Convert.FromHexString("83010203");
        ReadOnlyMemory<byte> second = Convert.FromHexString("83040506");

        CborReader reader = new(first, CborSerializerOptions.Default(CborConformanceMode.Lax));
        Assert.AreEqual(3, reader.ReadStartArray());
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual(2UL, reader.ReadUInt64());
        Assert.AreEqual(3UL, reader.ReadUInt64());
        reader.ReadEndArray();
        Assert.AreEqual(CborReaderState.Finished, reader.PeekState());

        reader.Reset(second);
        Assert.AreEqual(0, reader.CurrentDepth);
        Assert.AreEqual(0, reader.BytesConsumed);

        Assert.AreEqual(3, reader.ReadStartArray());
        Assert.AreEqual(4UL, reader.ReadUInt64());
        Assert.AreEqual(5UL, reader.ReadUInt64());
        Assert.AreEqual(6UL, reader.ReadUInt64());
        reader.ReadEndArray();
    }

    [TestMethod]
    public void ResetMidStreamDiscardsOpenContainers()
    {
        //Open an array, read one item, then reset — the second source
        //should parse cleanly without complaining about the abandoned
        //container.
        ReadOnlyMemory<byte> first = Convert.FromHexString("83010203");
        ReadOnlyMemory<byte> second = Convert.FromHexString("17");

        CborReader reader = new(first, CborSerializerOptions.Default(CborConformanceMode.Lax));
        Assert.AreEqual(3, reader.ReadStartArray());
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual(1, reader.CurrentDepth);

        reader.Reset(second);
        Assert.AreEqual(0, reader.CurrentDepth);
        Assert.AreEqual(23UL, reader.ReadUInt64());
        Assert.AreEqual(CborReaderState.Finished, reader.PeekState());
    }
}
