using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// Tests for the caller-provided-table mechanism per W3C CBOR-LD 1.0.
/// Registry entries may mark one or more <c>typeTables</c> entries as
/// the sentinel <c>"callerProvidedTable"</c> (modelled as
/// <see cref="CborLdCallerProvidedTypeTableMarker"/>); the caller then
/// supplies the actual mappings at encode and decode time via
/// <see cref="CborLdCallerProvidedTypeTables"/>.
/// </summary>
[TestClass]
internal sealed class CborLdCallerProvidedTableTests
{
    public required TestContext TestContext { get; set; }

    private const string UrlExample = "https://issuer.example/credential/1";
    private const string OtherUrlExample = "https://issuer.example/credential/2";

    private static FrozenDictionary<string, int> SingleUrlMapping { get; } =
        new Dictionary<string, int> { [UrlExample] = 200 }.ToFrozenDictionary();

    [TestMethod]
    public async Task CallerProvidedTableCompressesAndRoundTrips()
    {
        //Registry declares "url" as caller-provided. The caller supplies a
        //table mapping the document's URL to integer 200. Round-trip
        //recovers the URL string.
        CborLdRegistryEntry entry = BuildEntryWithCallerProvidedUrl();
        CborLdCallerProvidedTypeTables tables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(CborLdTestSetup.UrlType, SingleUrlMapping));

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        CborLdInputMap decoded = await RoundTripAsync(input, entry, tables).ConfigureAwait(false);

        Assert.HasCount(1, decoded.Entries);
        Assert.AreEqual("link", decoded.Entries[0].Key);
        Assert.AreEqual(UrlExample, ((CborLdInputString)decoded.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task MixedRegistryAndCallerProvidedSourcesBothWork()
    {
        //Registry has one fully-specified table (xsd:dateTime sentinel)
        //and one caller-provided marker (url). A document using both
        //types compresses and round-trips correctly with the caller
        //supplying the URL table.
        CborLdRegistryEntry entry = new(
            registryEntryId: 1,
            keywords: new Dictionary<string, CborLdKeywordCodec>(),
            terms: new Dictionary<string, CborLdTermCodec>
            {
                ["link"] = new("link", 100, CborLdTestSetup.UrlType),
                ["issued"] = new("issued", 102, CborLdTestSetup.DateTimeType)
            },
            processingModel: "default",
            provisional: false,
            typeTables: new Dictionary<string, CborLdTypeTableSource>
            {
                [CborLdTestSetup.UrlType] = CborLdTypeTableSource.CallerProvided(),
                [CborLdContextKeys.TypesEncodedAsBytesSentinel] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int>
                    {
                        [CborLdTestSetup.UrlType] = 1,
                        [CborLdTestSetup.DateTimeType] = 1
                    })
            });

        CborLdCallerProvidedTypeTables tables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(CborLdTestSetup.UrlType, SingleUrlMapping));

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample)),
            new KeyValuePair<string, CborLdInputNode>("issued", new CborLdInputString("2024-05-12T14:30:00Z"))
        });

        CborLdInputMap decoded = await RoundTripAsync(input, entry, tables).ConfigureAwait(false);

        Assert.HasCount(2, decoded.Entries);
        Assert.AreEqual(UrlExample, ((CborLdInputString)decoded.Entries[0].Value).Value);
        Assert.AreEqual("2024-05-12T14:30:00Z", ((CborLdInputString)decoded.Entries[1].Value).Value);
    }

    [TestMethod]
    public async Task CallerTableOmittingValueFallsThrough()
    {
        //Caller table maps UrlExample only; the document carries
        //OtherUrlExample which is absent from the table. The encoder
        //skips the table substitution and dispatches the URL codec on
        //the raw string. The URL codec rejects non-int inputs, so this
        //surfaces as a CborLdProcessingException from the codec
        //resolution path — not from the caller-table mechanism. We
        //assert that the failure isn't the missing-table error code.
        CborLdRegistryEntry entry = BuildEntryWithCallerProvidedUrl();
        CborLdCallerProvidedTypeTables tables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(CborLdTestSetup.UrlType, SingleUrlMapping));

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(OtherUrlExample))
        });

        ArrayBufferWriter<byte> buffer = new();
        CborLdProcessingException ex = await Assert.ThrowsExactlyAsync<CborLdProcessingException>(async () =>
            await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer, callerTables: tables).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreNotEqual("caller provided type table missing", ex.ErrorCode);
    }

    [TestMethod]
    public async Task CallerTableUnusedMappingIsIgnored()
    {
        //Caller supplies a table with two mappings; the document only
        //uses one. The other mapping is silently ignored — no exception.
        FrozenDictionary<string, int> twoMappings =
            new Dictionary<string, int>
            {
                [UrlExample] = 200,
                [OtherUrlExample] = 202
            }.ToFrozenDictionary();
        CborLdRegistryEntry entry = BuildEntryWithCallerProvidedUrl();
        CborLdCallerProvidedTypeTables tables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(CborLdTestSetup.UrlType, twoMappings));

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        CborLdInputMap decoded = await RoundTripAsync(input, entry, tables).ConfigureAwait(false);
        Assert.AreEqual(UrlExample, ((CborLdInputString)decoded.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task MissingCallerTableRaisesSpecAlignedException()
    {
        //Registry declares "url" as caller-provided but the caller passes
        //null caller tables. Expected: CborLdProcessingException with
        //code "caller provided type table missing", message naming the
        //type.
        CborLdRegistryEntry entry = BuildEntryWithCallerProvidedUrl();
        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        ArrayBufferWriter<byte> buffer = new();
        CborLdProcessingException ex = await Assert.ThrowsExactlyAsync<CborLdProcessingException>(async () =>
            await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreEqual("caller provided type table missing", ex.ErrorCode);
        Assert.Contains(CborLdTestSetup.UrlType, ex.Message);
    }

    [TestMethod]
    public async Task CallerSuppliesTableForWrongTypeRaisesSpecAlignedException()
    {
        //Registry declares "url" as caller-provided. The caller supplies
        //a table for a different type ("xsd:date"). Expected: same
        //missing-table exception, naming "url" — not "xsd:date".
        CborLdRegistryEntry entry = BuildEntryWithCallerProvidedUrl();
        CborLdCallerProvidedTypeTables tables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(
                CborLdTestSetup.DateType,
                new Dictionary<string, int> { ["1990-01-15"] = 7320 }.ToFrozenDictionary()));

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        ArrayBufferWriter<byte> buffer = new();
        CborLdProcessingException ex = await Assert.ThrowsExactlyAsync<CborLdProcessingException>(async () =>
            await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer, callerTables: tables).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreEqual("caller provided type table missing", ex.ErrorCode);
        Assert.Contains(CborLdTestSetup.UrlType, ex.Message);
    }

    [TestMethod]
    public async Task DecodeDerivesReverseLookupFromCallerForward()
    {
        //Caller supplies only the forward mapping (string → int). The
        //decoder must derive the inverse to recover the original URL.
        CborLdRegistryEntry entry = BuildEntryWithCallerProvidedUrl();
        CborLdCallerProvidedTypeTables encodeTables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(CborLdTestSetup.UrlType, SingleUrlMapping));

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer, callerTables: encodeTables).ConfigureAwait(false);

        //Decode with a freshly-constructed tables collection holding the
        //same forward mapping — proves the decoder doesn't rely on the
        //encoder's prior call having warmed any cached state.
        CborLdCallerProvidedTypeTables decodeTables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(CborLdTestSetup.UrlType, SingleUrlMapping));

        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(id == entry.RegistryEntryId ? entry : null),
            callerTables: decodeTables,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        CborLdInputMap decoded = (CborLdInputMap)result.Root;
        Assert.AreEqual(UrlExample, ((CborLdInputString)decoded.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task WireFormMatchesRegistryProvidedEquivalent()
    {
        //A document encoded with a caller-provided table for "url" with
        //the SAME content as a registry-supplied table must produce the
        //same wire bytes. This is the interoperability property: a
        //consumer using caller-provided tables emits bytes that a
        //consumer with the equivalent registry-supplied table reads
        //identically.
        CborLdRegistryEntry callerEntry = BuildEntryWithCallerProvidedUrl();
        CborLdRegistryEntry registryEntry = new(
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
                    new Dictionary<string, int> { [UrlExample] = 200 }),
                [CborLdContextKeys.TypesEncodedAsBytesSentinel] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [CborLdTestSetup.UrlType] = 1 })
            });
        CborLdCallerProvidedTypeTables tables = new CborLdCallerProvidedTypeTables()
            .Add(new CborLdCallerProvidedTypeTable(CborLdTestSetup.UrlType, SingleUrlMapping));

        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("link", new CborLdInputString(UrlExample))
        });

        ArrayBufferWriter<byte> callerBuffer = new();
        ArrayBufferWriter<byte> registryBuffer = new();
        await CborLdEncoder.EncodeAsync(input, callerEntry, CborLdProfile.Default, callerBuffer, callerTables: tables).ConfigureAwait(false);
        await CborLdEncoder.EncodeAsync(input, registryEntry, CborLdProfile.Default, registryBuffer).ConfigureAwait(false);

        Assert.AreSequenceEqual(registryBuffer.WrittenSpan.ToArray(), callerBuffer.WrittenSpan.ToArray());
    }

    /// <summary>
    /// Builds a registry entry that declares the URL type table as
    /// caller-provided and lists "url" in the encoded-as-bytes sentinel
    /// so the encoder emits a CBOR byte string for the URL wire form.
    /// </summary>
    private static CborLdRegistryEntry BuildEntryWithCallerProvidedUrl()
    {
        return new CborLdRegistryEntry(
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
                [CborLdTestSetup.UrlType] = CborLdTypeTableSource.CallerProvided(),
                [CborLdContextKeys.TypesEncodedAsBytesSentinel] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [CborLdTestSetup.UrlType] = 1 })
            });
    }

    private async ValueTask<CborLdInputMap> RoundTripAsync(
        CborLdInputMap input,
        CborLdRegistryEntry entry,
        CborLdCallerProvidedTypeTables tables)
    {
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(input, entry, CborLdProfile.Default, buffer, callerTables: tables).ConfigureAwait(false);

        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(id == entry.RegistryEntryId ? entry : null),
            callerTables: tables,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        return (CborLdInputMap)result.Root;
    }
}
