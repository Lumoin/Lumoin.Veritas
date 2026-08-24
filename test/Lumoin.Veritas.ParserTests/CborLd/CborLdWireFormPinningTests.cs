using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// Pins canonical CBOR-LD wire forms against hand-computed expected hex
/// strings. These tests serve as regression alarms: a future change that
/// alters the wire format must update the expected bytes explicitly.
/// </summary>
[TestClass]
internal sealed class CborLdWireFormPinningTests
{
    [TestMethod]
    public async Task PassthroughSimpleDocument()
    {
        //tag(51997, [0, {"a": 1}])
        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("a", new CborLdInputInt(1))
        });

        byte[] encoded = await EncodeAsync(input, CborLdRegistryEntry.Passthrough).ConfigureAwait(false);

        //Expected wire form:
        // D9 CB1D     -- tag 51997
        // 82          -- array(2)
        //   00          -- 0 (registry id)
        //   A1          -- map(1)
        //     61 61       -- "a"
        //     01          -- 1
        AssertHex("D9CB1D8200A1616101", encoded);
    }

    [TestMethod]
    public async Task CompressedRegistryTermsOnly()
    {
        //Registry entry with one term: "name" -> 100. Document {"name": "Alice"}.
        CborLdRegistryEntry entry = new(
            registryEntryId: 1,
            keywords: new Dictionary<string, CborLdKeywordCodec>(),
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["name"] = new("name", 100)
            });
        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        byte[] encoded = await EncodeAsync(input, entry).ConfigureAwait(false);

        //Expected wire form:
        // D9 CB1D       -- tag 51997
        // 82            -- array(2)
        //   01            -- 1 (registry id)
        //   A1            -- map(1)
        //     18 64         -- 100 (compressed key for "name")
        //     65 41 6C 69 63 65   -- "Alice"
        AssertHex("D9CB1D8201A118646541 6C696365".Replace(" ", string.Empty, StringComparison.Ordinal), encoded);
    }

    [TestMethod]
    public async Task CompressedWithTypedValueByteString()
    {
        //Registry entry: term "link" typed as "url"; type table maps the URL
        //to integer 300; "url" is in the types-encoded-as-bytes sentinel.
        const string TheUrl = "https://example.org/foo";
        CborLdRegistryEntry entry = new(
            registryEntryId: 1,
            keywords: new Dictionary<string, CborLdKeywordCodec>(),
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["link"] = new("link", 100, CborLdTestSetup.UrlType)
            },
            processingModel: "default",
            provisional: false,
            typeTables: new Dictionary<string, CborLdTypeTableSource>
            {
                [CborLdTestSetup.UrlType] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [TheUrl] = 300 }),
                [CborLdContextKeys.TypesEncodedAsBytesSentinel] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [CborLdTestSetup.UrlType] = 1 })
            });
        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(TheUrl))
        });

        byte[] encoded = await EncodeAsync(input, entry).ConfigureAwait(false);

        //Expected wire form:
        // D9 CB1D     -- tag 51997
        // 82          -- array(2)
        //   01          -- 1 (registry id)
        //   A1          -- map(1)
        //     18 64       -- 100 (compressed key for "link")
        //     42 01 2C     -- byte string(2) 0x01 0x2C (== 300 big-endian)
        AssertHex("D9CB1D8201A11864 4201 2C".Replace(" ", string.Empty, StringComparison.Ordinal), encoded);
    }

    [TestMethod]
    public async Task PassthroughEmptyMap()
    {
        //Smallest possible CBOR-LD document.
        CborLdInputMap input = new(System.Array.Empty<KeyValuePair<string, CborLdInputNode>>());
        byte[] encoded = await EncodeAsync(input, CborLdRegistryEntry.Passthrough).ConfigureAwait(false);

        //Expected:
        // D9 CB1D   -- tag 51997
        // 82        -- array(2)
        //   00        -- 0 (passthrough id)
        //   A0        -- map(0)
        AssertHex("D9CB1D8200A0", encoded);
    }

    /// <summary>Encodes <paramref name="input"/> against <paramref name="entry"/> and returns the written wire bytes.</summary>
    /// <param name="input">The document tree to encode.</param>
    /// <param name="entry">The registry entry to encode against.</param>
    /// <returns>The encoded wire bytes.</returns>
    private static async Task<byte[]> EncodeAsync(CborLdInputNode input, CborLdRegistryEntry entry)
    {
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        return buffer.WrittenSpan.ToArray();
    }

    private static void AssertHex(string expectedHex, byte[] actual)
    {
        string actualHex = Convert.ToHexString(actual);
        Assert.AreEqual(expectedHex.ToUpperInvariant(), actualHex);
    }
}
