using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.CborLd;

[TestClass]
internal sealed class CborLdTypedValueCodecRoundTripTests
{
    public required TestContext TestContext { get; set; }

    private const string UrlExample = "https://example.org/foo";
    private const string DateTimeExample = "2024-05-12T14:30:00Z";

    [TestMethod]
    public async Task UrlRoundTripWithIntegerEncoding()
    {
        //Registry maps the URL to integer 200. Type "url" is NOT in the
        //types-encoded-as-bytes sentinel, so the wire form is a CBOR integer.
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["link"] = new("link", 100, CborLdTestSetup.UrlType)
            },
            typeTables: new Dictionary<string, CborLdTypeTableSource>
            {
                [CborLdTestSetup.UrlType] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [UrlExample] = 200 })
            });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);

        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.HasCount(1, roundTripped.Entries);
        Assert.AreEqual("link", roundTripped.Entries[0].Key);
        Assert.AreEqual(UrlExample, ((CborLdInputString)roundTripped.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task UrlRoundTripWithByteStringEncoding()
    {
        //Add the sentinel that marks "url" as encoded-as-bytes. The encoder
        //emits a CBOR byte string at the value position instead of an integer.
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["link"] = new("link", 100, CborLdTestSetup.UrlType)
            },
            typeTables: new Dictionary<string, CborLdTypeTableSource>
            {
                [CborLdTestSetup.UrlType] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [UrlExample] = 200 }),
                [CborLdContextKeys.TypesEncodedAsBytesSentinel] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [CborLdTestSetup.UrlType] = 1 })
            });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);

        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.AreEqual(UrlExample, ((CborLdInputString)roundTripped.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task UnknownTypeProducesProcessingException()
    {
        //Type "urn:custom:unknown" is not in the test setup matcher; the
        //encoder must surface a CborLdProcessingException naming the
        //term, with the original ArgumentException as inner.
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["weird"] = new("weird", 100, "urn:custom:unknown")
            },
            typeTables: new Dictionary<string, CborLdTypeTableSource>
            {
                ["urn:custom:unknown"] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { ["foo"] = 200 })
            });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("weird", new CborLdInputString("foo"))
        });

        ArrayBufferWriter<byte> buffer = new();
        CborLdProcessingException ex = await Assert.ThrowsExactlyAsync<CborLdProcessingException>(async () =>
            await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.IsNotNull(ex.InnerException);
        Assert.IsInstanceOfType<ArgumentException>(ex.InnerException);
    }

    [TestMethod]
    public async Task UntypedFieldPassesThrough()
    {
        //Term without a Type binding; encoder must not consult the codec
        //registry, and the value must round-trip unchanged.
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["name"] = new("name", 100)   //no Type
            });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);

        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.AreEqual("Alice", ((CborLdInputString)roundTripped.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task DateTimeRoundTripViaCodec()
    {
        //DateTime is a direct-conversion type: the codec parses the
        //string itself; no type-table mapping is required. The wire
        //form is a CBOR byte string carrying epoch seconds per
        //§5.2.1 step 4.
        CborLdRegistryEntry entry = BuildEntry(
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["issued"] = new("issued", 100, CborLdTestSetup.DateTimeType)
            },
            typeTables: new Dictionary<string, CborLdTypeTableSource>
            {
                [CborLdContextKeys.TypesEncodedAsBytesSentinel] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [CborLdTestSetup.DateTimeType] = 1 })
            });

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("issued", new CborLdInputString(DateTimeExample))
        });

        CborLdDecodeResult result = await RoundTripAsync(input, entry).ConfigureAwait(false);

        CborLdInputMap roundTripped = (CborLdInputMap)result.Root;
        Assert.AreEqual(DateTimeExample, ((CborLdInputString)roundTripped.Entries[0].Value).Value);
    }

    private static CborLdRegistryEntry BuildEntry(
        IReadOnlyDictionary<string, CborLdKeywordCodec>? keywords = null,
        IReadOnlyDictionary<string, CborLdTermCodec>? terms = null,
        IReadOnlyDictionary<string, CborLdTypeTableSource>? typeTables = null)
    {
        return new CborLdRegistryEntry(
            registryEntryId: 1,
            keywords: keywords ?? new Dictionary<string, CborLdKeywordCodec>(),
            terms: terms ?? new Dictionary<string, CborLdTermCodec>(),
            processingModel: "default",
            provisional: false,
            typeTables: typeTables);
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
