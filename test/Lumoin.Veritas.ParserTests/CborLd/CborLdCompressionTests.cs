using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.CborLd;

[TestClass]
internal sealed class CborLdCompressionTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task SingularTermEncodesAsEvenIntegerKey()
    {
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec> { ["name"] = new("name", 100) });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);

        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.HasCount(1, roundTripped.Entries);
        Assert.AreEqual("name", roundTripped.Entries[0].Key);
        Assert.AreEqual("Alice", ((CborLdInputString)roundTripped.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task PluralTermEncodesAsOddIntegerKey()
    {
        //An array-valued entry under "tags" should encode the key as 101 (odd plural of 100).
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec> { ["tags"] = new("tags", 100) });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("tags", new CborLdInputArray(new CborLdInputNode[]
            {
                new CborLdInputString("a"),
                new CborLdInputString("b")
            }))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        //Decode and assert structural equality.
        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            BuildLoader(entry),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.HasCount(1, roundTripped.Entries);
        Assert.AreEqual("tags", roundTripped.Entries[0].Key);
        Assert.IsInstanceOfType<CborLdInputArray>(roundTripped.Entries[0].Value);
    }

    [TestMethod]
    public async Task UnknownKeyEmitsAsTextString()
    {
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec> { ["name"] = new("name", 100) });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice")),
            new KeyValuePair<string, CborLdInputNode>("unknown", new CborLdInputString("X"))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);

        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.HasCount(2, roundTripped.Entries);
        Dictionary<string, string> byKey = new();
        foreach(KeyValuePair<string, CborLdInputNode> e in roundTripped.Entries)
        {
            byKey[e.Key] = ((CborLdInputString)e.Value).Value;
        }
        Assert.AreEqual("Alice", byKey["name"]);
        Assert.AreEqual("X", byKey["unknown"]);
    }

    [TestMethod]
    public async Task NestedMapAlsoCompresses()
    {
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["outer"] = new("outer", 100),
                ["inner"] = new("inner", 102)
            });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("outer", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("inner", new CborLdInputInt(7))
            }))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);
        CborLdInputMap outer = (CborLdInputMap)result.Root;
        CborLdInputMap inner = (CborLdInputMap)outer.Entries[0].Value;
        Assert.AreEqual("outer", outer.Entries[0].Key);
        Assert.AreEqual("inner", inner.Entries[0].Key);
        Assert.AreEqual(7L, ((CborLdInputInt)inner.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task KeywordCompressesUsingRegistryEntryKeyword()
    {
        CborLdRegistryEntry entry = BuildEntry(
            keywords: new Dictionary<string, CborLdKeywordCodec> { ["@id"] = new("@id", 4) });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("https://example.org/foo"))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);
        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.AreEqual("@id", roundTripped.Entries[0].Key);
    }

    private static CborLdRegistryEntry BuildEntry(
        IReadOnlyDictionary<string, CborLdKeywordCodec>? keywords = null,
        IReadOnlyDictionary<string, CborLdTermCodec>? terms = null)
    {
        return new CborLdRegistryEntry(
            registryEntryId: 1,
            keywords: keywords ?? new Dictionary<string, CborLdKeywordCodec>(),
            terms: terms ?? new Dictionary<string, CborLdTermCodec>());
    }

    private static LoadCborLdRegistryEntryDelegate BuildLoader(CborLdRegistryEntry entry)
    {
        return (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(id == entry.RegistryEntryId ? entry : null);
    }

    private async Task<CborLdDecodeResult> RoundTripAsync(CborLdInputNode root, CborLdRegistryEntry entry)
    {
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(root, entry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        return await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            BuildLoader(entry),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }
}
