using System.Buffers;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// E3 step-3 verification: the encoder walks a document, applies embedded
/// <c>@context</c>, eagerly assigns dynamic ids for context-defined terms,
/// and emits those terms as integer keys on the wire. The decoder is not
/// yet wired (step 4), so these tests inspect the raw wire form via the
/// BCL <see cref="CborReader"/> instead of round-tripping through the
/// Lumoin decoder.
/// </summary>
[TestClass]
internal sealed class CborLdEncoderE3Tests
{
    public required TestContext TestContext { get; set; }

    /// <summary>
    /// A registry entry with only a <c>@context</c> keyword codec defined —
    /// enough to flip the encoder into compressed mode without pre-binding
    /// any user terms. Any term that compresses must therefore come from
    /// the active context at runtime.
    /// </summary>
    private static CborLdRegistryEntry KeywordOnlyEntry { get; } = new(
        registryEntryId: 1,
        keywords: new Dictionary<string, CborLdKeywordCodec>
        {
            ["@context"] = new CborLdKeywordCodec("@context", 0),
            ["@id"] = new CborLdKeywordCodec("@id", 2),
            ["@type"] = new CborLdKeywordCodec("@type", 4)
        },
        terms: new Dictionary<string, CborLdTermCodec>());

    [TestMethod]
    public async Task DynamicallyDefinedTermCompressesToIntegerKey()
    {
        //Document defines "name" in @context and uses it as a top-level key.
        //Encoder should assign a dynamic id (100, the first slot) and emit
        //"name" as an integer wire key.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("http://schema.org/name"))
            })),
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, KeywordOnlyEntry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        AssertWireForm(buffer.WrittenSpan, keys: ["@context-id=0", "name-id=100"]);
    }

    [TestMethod]
    public async Task ContextValueInnerKeysEmitAsTextNotDynamicIds()
    {
        //Critical correctness check: the term "name" defined inside the
        //@context value must NOT be compressed back to its freshly-allocated
        //dynamic id (100). The @context value is encoded in the
        //PRE-embedded active context (empty here), where "name" is not yet
        //a visible term — so it falls through to text-string emission.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("http://schema.org/name"))
            }))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, KeywordOnlyEntry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        CborReader reader = OpenWirePayload(buffer.WrittenSpan);
        int mapCount = reader.ReadStartMap() ?? 0;
        Assert.AreEqual(1, mapCount);

        //Outer key: @context as integer 0.
        Assert.AreEqual(CborReaderState.UnsignedInteger, reader.PeekState());
        Assert.AreEqual(0, reader.ReadInt32());

        //Inner map: the @context value.
        int innerCount = reader.ReadStartMap() ?? 0;
        Assert.AreEqual(1, innerCount);

        //Inner key MUST be text "name" — the definition position is not
        //compressible regardless of what the active context will contain.
        Assert.AreEqual(CborReaderState.TextString, reader.PeekState());
        Assert.AreEqual("name", reader.ReadTextString());
        Assert.AreEqual("http://schema.org/name", reader.ReadTextString());
        reader.ReadEndMap();
        reader.ReadEndMap();
    }

    [TestMethod]
    public async Task EncoderDecoderIdAlignmentForMultipleTerms()
    {
        //Two @context entries, three new terms total. Assignment order must
        //be deterministic and reproducible — the same document encoded twice
        //must produce the same wire bytes, because the walk applies contexts
        //in document order and AssignTermId is idempotent.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("alpha", new CborLdInputString("http://example.org/alpha")),
                new KeyValuePair<string, CborLdInputNode>("beta", new CborLdInputString("http://example.org/beta"))
            })),
            new KeyValuePair<string, CborLdInputNode>("alpha", new CborLdInputString("A")),
            new KeyValuePair<string, CborLdInputNode>("beta", new CborLdInputString("B"))
        });

        ArrayBufferWriter<byte> first = new();
        ArrayBufferWriter<byte> second = new();
        await CborLdEncoder.EncodeAsync(doc, KeywordOnlyEntry, CborLdProfile.Default, first).ConfigureAwait(false);
        await CborLdEncoder.EncodeAsync(doc, KeywordOnlyEntry, CborLdProfile.Default, second).ConfigureAwait(false);

        Assert.AreEqual(first.WrittenCount, second.WrittenCount);
        Assert.AreSequenceEqual(first.WrittenSpan.ToArray(), second.WrittenSpan.ToArray());
    }

    [TestMethod]
    public async Task RoundTripWithDynamicallyDefinedTerm()
    {
        //End-to-end E3 verification: encode a doc that defines a term in
        //@context and uses it, then decode the wire bytes back to a tree.
        //The decoded tree must contain the original key names, proving:
        //  (1) the encoder allocates a dynamic id and emits it on the wire,
        //  (2) the decoder reads the @context first, applies it via
        //      WithEmbeddedContextAsync, and resolves subsequent integer
        //      keys against the dynamic-id table.
        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("http://schema.org/name")),
                new KeyValuePair<string, CborLdInputNode>("age", new CborLdInputString("http://schema.org/age"))
            })),
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice")),
            new KeyValuePair<string, CborLdInputNode>("age", new CborLdInputInt(30))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(input, KeywordOnlyEntry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(id == KeywordOnlyEntry.RegistryEntryId ? KeywordOnlyEntry : null),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        CborLdInputMap decoded = (CborLdInputMap)result.Root;
        Assert.HasCount(3, decoded.Entries);
        //Decoded entry order matches wire order: @context first (by reorder),
        //then name and age.
        Assert.AreEqual("@context", decoded.Entries[0].Key);
        Assert.AreEqual("name", decoded.Entries[1].Key);
        Assert.AreEqual("age", decoded.Entries[2].Key);
        Assert.AreEqual("Alice", ((CborLdInputString)decoded.Entries[1].Value).Value);
        Assert.AreEqual(30L, ((CborLdInputInt)decoded.Entries[2].Value).Value);
    }

    [TestMethod]
    public async Task RoundTripPreservesDocumentOrderInsideContextValue()
    {
        //The @context value's inner map is encoded in pre-embedded
        //context, so its keys emit as text strings. The decoder reads
        //them back as text — definition position is invariant under E3.
        CborLdInputMap input = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("alpha", new CborLdInputString("http://example.org/alpha")),
                new KeyValuePair<string, CborLdInputNode>("beta", new CborLdInputString("http://example.org/beta"))
            }))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(input, KeywordOnlyEntry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(id == KeywordOnlyEntry.RegistryEntryId ? KeywordOnlyEntry : null),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        CborLdInputMap decoded = (CborLdInputMap)result.Root;
        Assert.HasCount(1, decoded.Entries);
        Assert.AreEqual("@context", decoded.Entries[0].Key);
        CborLdInputMap ctxValue = (CborLdInputMap)decoded.Entries[0].Value;
        Assert.HasCount(2, ctxValue.Entries);
        Assert.AreEqual("alpha", ctxValue.Entries[0].Key);
        Assert.AreEqual("beta", ctxValue.Entries[1].Key);
    }

    /// <summary>
    /// Opens the CBOR-LD wire envelope and positions the reader at the
    /// document payload (the second element of the outer array, after the
    /// registry-entry-id). Returns a reader ready to start reading the
    /// payload map.
    /// </summary>
    private static CborReader OpenWirePayload(System.ReadOnlySpan<byte> wire)
    {
        CborReader reader = new(wire.ToArray());
        _ = reader.ReadTag(); //0xCB1D
        _ = reader.ReadStartArray();
        _ = reader.ReadInt32(); //registry entry id
        return reader;
    }

    private static void AssertWireForm(System.ReadOnlySpan<byte> wire, string[] keys)
    {
        CborReader reader = OpenWirePayload(wire);
        int mapCount = reader.ReadStartMap() ?? 0;
        Assert.AreEqual(keys.Length, mapCount);

        foreach(string expectation in keys)
        {
            //expectation format: "name-id=N" → expect integer N as key.
            int eq = expectation.IndexOf('=', System.StringComparison.Ordinal);
            Assert.IsGreaterThan(0, eq);
            int expectedId = int.Parse(expectation[(eq + 1)..], System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual(CborReaderState.UnsignedInteger, reader.PeekState());
            int actualKey = reader.ReadInt32();
            Assert.AreEqual(expectedId, actualKey, $"key '{expectation}' wrong on wire");

            reader.SkipValue();
        }

        reader.ReadEndMap();
    }
}
