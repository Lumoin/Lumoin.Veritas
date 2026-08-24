using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

[TestClass]
internal sealed class CborLdRoundTripTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PassthroughEncodesOuterTagAndRegistryEntryId()
    {
        CborLdInputNode root = new CborLdInputInt(42);
        ArrayBufferWriter<byte> buffer = new();

        await CborLdEncoder.EncodeAsync(
            root,
            CborLdRegistryEntry.Passthrough,
            CborLdProfile.Default,
            buffer).ConfigureAwait(false);

        byte[] bytes = buffer.WrittenSpan.ToArray();
        //Wire prefix per W3C CBOR-LD 1.0 §5.6.1: D9 CB 1D 82 followed by
        //the registry entry id (here 0x00) and the payload (here 0x18 2A).
        Assert.AreEqual((byte)0xD9, bytes[0]);
        Assert.AreEqual((byte)0xCB, bytes[1]);
        Assert.AreEqual((byte)0x1D, bytes[2]);
        Assert.AreEqual((byte)0x82, bytes[3]);
        Assert.AreEqual((byte)0x00, bytes[4]);   //registry entry id 0
        Assert.AreEqual((byte)0x18, bytes[5]);   //unsigned int, one-byte argument
        Assert.AreEqual((byte)0x2A, bytes[6]);   //value 42
    }

    [TestMethod]
    public async Task RoundTripPrimitiveInteger()
    {
        CborLdInputNode root = new CborLdInputInt(12345);
        CborLdDecodeResult result = await EncodeThenDecodeAsync(root).ConfigureAwait(false);

        Assert.AreEqual(0, result.RegistryEntryId);
        Assert.IsInstanceOfType<CborLdInputInt>(result.Root);
        Assert.AreEqual(12345L, ((CborLdInputInt)result.Root).Value);
    }

    [TestMethod]
    public async Task RoundTripBooleanAndNull()
    {
        CborLdInputNode boolNode = new CborLdInputBool(true);
        CborLdDecodeResult boolResult = await EncodeThenDecodeAsync(boolNode).ConfigureAwait(false);
        Assert.IsInstanceOfType<CborLdInputBool>(boolResult.Root);
        Assert.IsTrue(((CborLdInputBool)boolResult.Root).Value);

        CborLdDecodeResult nullResult = await EncodeThenDecodeAsync(CborLdInputNull.Instance).ConfigureAwait(false);
        Assert.IsInstanceOfType<CborLdInputNull>(nullResult.Root);
    }

    [TestMethod]
    public async Task RoundTripTextString()
    {
        CborLdInputNode root = new CborLdInputString("hello world");
        CborLdDecodeResult result = await EncodeThenDecodeAsync(root).ConfigureAwait(false);

        Assert.IsInstanceOfType<CborLdInputString>(result.Root);
        Assert.AreEqual("hello world", ((CborLdInputString)result.Root).Value);
    }

    [TestMethod]
    public async Task RoundTripArray()
    {
        CborLdInputArray root = new(
        [
            new CborLdInputInt(1),
            new CborLdInputString("two"),
            new CborLdInputBool(false),
            CborLdInputNull.Instance
        ]);

        CborLdDecodeResult result = await EncodeThenDecodeAsync(root).ConfigureAwait(false);

        Assert.IsInstanceOfType<CborLdInputArray>(result.Root);
        CborLdInputArray decoded = (CborLdInputArray)result.Root;
        Assert.HasCount(4, decoded.Items);
        Assert.AreEqual(1L, ((CborLdInputInt)decoded.Items[0]).Value);
        Assert.AreEqual("two", ((CborLdInputString)decoded.Items[1]).Value);
        Assert.IsFalse(((CborLdInputBool)decoded.Items[2]).Value);
        Assert.IsInstanceOfType<CborLdInputNull>(decoded.Items[3]);
    }

    [TestMethod]
    public async Task RoundTripMap()
    {
        CborLdInputMap root = new(
        [
            new("id", new CborLdInputString("urn:example:42")),
            new("count", new CborLdInputInt(7))
        ]);

        CborLdDecodeResult result = await EncodeThenDecodeAsync(root).ConfigureAwait(false);

        Assert.IsInstanceOfType<CborLdInputMap>(result.Root);
        CborLdInputMap decoded = (CborLdInputMap)result.Root;
        Assert.HasCount(2, decoded.Entries);
        Assert.AreEqual("id", decoded.Entries[0].Key);
        Assert.AreEqual("urn:example:42", ((CborLdInputString)decoded.Entries[0].Value).Value);
        Assert.AreEqual("count", decoded.Entries[1].Key);
        Assert.AreEqual(7L, ((CborLdInputInt)decoded.Entries[1].Value).Value);
    }

    [TestMethod]
    public async Task RoundTripNestedTree()
    {
        CborLdInputMap root = new(
        [
            new("@context", new CborLdInputString("https://example.org/context.jsonld")),
            new("@id", new CborLdInputString("urn:example:doc1")),
            new("name", new CborLdInputString("Example")),
            new("tags", new CborLdInputArray(
            [
                new CborLdInputString("alpha"),
                new CborLdInputString("beta"),
                new CborLdInputString("gamma")
            ])),
            new("nested", new CborLdInputMap(
            [
                new("level", new CborLdInputInt(2)),
                new("active", new CborLdInputBool(true))
            ]))
        ]);

        CborLdDecodeResult result = await EncodeThenDecodeAsync(root).ConfigureAwait(false);

        CborLdInputMap decoded = (CborLdInputMap)result.Root;
        Assert.HasCount(5, decoded.Entries);
        CborLdInputArray tags = (CborLdInputArray)decoded.Entries[3].Value;
        Assert.HasCount(3, tags.Items);
        CborLdInputMap nested = (CborLdInputMap)decoded.Entries[4].Value;
        Assert.AreEqual(2L, ((CborLdInputInt)nested.Entries[0].Value).Value);
        Assert.IsTrue(((CborLdInputBool)nested.Entries[1].Value).Value);
    }

    [TestMethod]
    public async Task DeterministicProfileProducesByteIdenticalEncodings()
    {
        //Two different in-memory map insertion orders should produce the
        //same bytes under the Deterministic profile because the inner
        //CBOR layer runs under CDE and sorts map keys bytewise.
        CborLdInputMap orderA = new(
        [
            new("b", new CborLdInputInt(2)),
            new("a", new CborLdInputInt(1))
        ]);
        CborLdInputMap orderB = new(
        [
            new("a", new CborLdInputInt(1)),
            new("b", new CborLdInputInt(2))
        ]);

        byte[] bytesA = await EncodeAsync(orderA, CborLdProfile.Deterministic).ConfigureAwait(false);
        byte[] bytesB = await EncodeAsync(orderB, CborLdProfile.Deterministic).ConfigureAwait(false);

        Assert.AreSequenceEqual(bytesA, bytesB);
    }

    [TestMethod]
    public async Task DecodeRejectsInputWithoutOuterTag()
    {
        //Bytes encode a bare integer with no CBOR-LD wrapper.
        ReadOnlyMemory<byte> bytes = new byte[] { 0x01 };

        await Assert.ThrowsAsync<CborLdProcessingException>(
            async () => await CborLdDecoder.DecodeAsync(
                bytes,
                CborLdRegistry.Empty.AsDelegate(),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DecodeRejectsUnknownRegistryEntryId()
    {
        //Build a valid wire form that names a registry entry the loader does not know.
        CborLdRegistryEntry entry = new(
            registryEntryId: 99,
            keywords: new Dictionary<string, CborLdKeywordCodec>(),
            terms: new Dictionary<string, CborLdTermCodec>());
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(new CborLdInputInt(1), entry, CborLdProfile.Default, buffer).ConfigureAwait(false);
        ReadOnlyMemory<byte> bytes = buffer.WrittenMemory;

        //Loader that always returns null.
        async ValueTask<CborLdRegistryEntry?> NullLoader(int id, CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return null;
        }

        await Assert.ThrowsAsync<CborLdProcessingException>(
            async () => await CborLdDecoder.DecodeAsync(
                bytes,
                NullLoader,
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EncodeAcceptsCompressionRegistryEntry()
    {
        //An entry with non-empty codec tables now routes through the
        //compression path. Encoding a primitive succeeds; the wire form
        //carries the entry's registry id rather than 0.
        CborLdRegistryEntry compressionEntry = new(
            registryEntryId: 1,
            keywords: new Dictionary<string, CborLdKeywordCodec>
            {
                ["@id"] = new("@id", 0)
            },
            terms: new Dictionary<string, CborLdTermCodec>());
        ArrayBufferWriter<byte> buffer = new();

        await CborLdEncoder.EncodeAsync(
            new CborLdInputInt(1),
            compressionEntry,
            CborLdProfile.Default,
            buffer).ConfigureAwait(false);

        Assert.IsGreaterThan(0, buffer.WrittenCount);
    }

    private async ValueTask<CborLdDecodeResult> EncodeThenDecodeAsync(CborLdInputNode root)
    {
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(root, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer).ConfigureAwait(false);

        return await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            CborLdRegistry.Empty.AsDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Encodes <paramref name="root"/> as passthrough under <paramref name="profile"/> and returns the written wire bytes.</summary>
    /// <param name="root">The document tree to encode.</param>
    /// <param name="profile">The encoding profile.</param>
    /// <returns>The encoded wire bytes.</returns>
    private static async Task<byte[]> EncodeAsync(CborLdInputNode root, CborLdProfile profile)
    {
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(root, CborLdRegistryEntry.Passthrough, profile, buffer).ConfigureAwait(false);

        return buffer.WrittenSpan.ToArray();
    }
}
