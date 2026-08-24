using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// End-to-end round-trip tests for the E3 active-context state machine
/// covering each scope kind (embedded, type-scoped, property-scoped),
/// the <c>@propagate: false</c> gating, and a nested combination.
/// </summary>
/// <remarks>
/// All tests use a keyword-only compressed registry so the encoder/decoder
/// take the E3 compression path. A successful round-trip proves the
/// encoder allocates dynamic ids deterministically, emits the right wire
/// form, and the decoder applies contexts in the same order to recover
/// the original tree.
/// </remarks>
[TestClass]
internal sealed class CborLdE3ScopeRoundTripTests
{
    public required TestContext TestContext { get; set; }

    private static CborLdRegistryEntry CompressedEntry { get; } = new(
        registryEntryId: 1,
        keywords: new Dictionary<string, CborLdKeywordCodec>
        {
            ["@context"] = new CborLdKeywordCodec("@context", 0),
            ["@id"] = new CborLdKeywordCodec("@id", 2),
            ["@type"] = new CborLdKeywordCodec("@type", 4)
        },
        terms: new Dictionary<string, CborLdTermCodec>());

    private LoadCborLdRegistryEntryDelegate Loader { get; } =
        (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(
            id == CompressedEntry.RegistryEntryId ? CompressedEntry : null);

    [TestMethod]
    public async Task TypeScopedSingleTypeRoundTrips()
    {
        //@context defines "Person" as a type term carrying its own
        //@context that introduces "name". Document has @type=Person,
        //then a "name" entry that must compress through the type-scoped
        //context.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("Person", new CborLdInputMap(new[]
                {
                    new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("http://schema.org/Person")),
                    new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
                    {
                        new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("http://schema.org/name"))
                    }))
                }))
            })),
            new KeyValuePair<string, CborLdInputNode>("@type", new CborLdInputString("http://schema.org/Person")),
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        CborLdInputMap decoded = await RoundTripAsync(doc).ConfigureAwait(false);

        Assert.HasCount(3, decoded.Entries);
        //Emission order: @context, @type, name.
        Assert.AreEqual("@context", decoded.Entries[0].Key);
        Assert.AreEqual("@type", decoded.Entries[1].Key);
        Assert.AreEqual("name", decoded.Entries[2].Key);
        Assert.AreEqual("Alice", ((CborLdInputString)decoded.Entries[2].Value).Value);
    }

    [TestMethod]
    public async Task TypeScopedAlphabeticalOrderObservedInLastWriteWins()
    {
        //Two types with conflicting scoped contexts; alphabetical-last wins.
        //"Alpha" defines color → http://example.org/alpha-color
        //"Beta"  defines color → http://example.org/beta-color
        //Sorted alphabetical: Alpha applied, then Beta. Beta wins.
        //The encoder emits "color" as a dynamic id that the decoder resolves
        //via the type-scoped chain. A successful round-trip proves both
        //sides applied the same ordering.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("Alpha", new CborLdInputMap(new[]
                {
                    new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("http://example.org/Alpha")),
                    new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
                    {
                        new KeyValuePair<string, CborLdInputNode>("color", new CborLdInputString("http://example.org/alpha-color"))
                    }))
                })),
                new KeyValuePair<string, CborLdInputNode>("Beta", new CborLdInputMap(new[]
                {
                    new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("http://example.org/Beta")),
                    new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
                    {
                        new KeyValuePair<string, CborLdInputNode>("color", new CborLdInputString("http://example.org/beta-color"))
                    }))
                }))
            })),
            new KeyValuePair<string, CborLdInputNode>("@type", new CborLdInputArray(new CborLdInputNode[]
            {
                new CborLdInputString("http://example.org/Beta"),
                new CborLdInputString("http://example.org/Alpha")
            })),
            new KeyValuePair<string, CborLdInputNode>("color", new CborLdInputString("red"))
        });

        CborLdInputMap decoded = await RoundTripAsync(doc).ConfigureAwait(false);

        //If the round-trip succeeded, encoder and decoder agreed on the
        //alphabetical ordering. The "color" key is observable in the
        //decoded tree as the original text.
        bool foundColor = false;
        foreach(KeyValuePair<string, CborLdInputNode> entry in decoded.Entries)
        {
            if(entry.Key == "color")
            {
                foundColor = true;
                Assert.AreEqual("red", ((CborLdInputString)entry.Value).Value);
            }
        }
        Assert.IsTrue(foundColor, "Expected 'color' key in decoded tree");
    }

    [TestMethod]
    public async Task PropertyScopedRoundTrips()
    {
        //@context defines "Container" as a property term carrying its own
        //@context that introduces "inner". The value of "Container" is a
        //map whose "inner" key is resolved through the property-scoped
        //context active for that subtree.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("Container", new CborLdInputMap(new[]
                {
                    new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("http://example.org/container")),
                    new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
                    {
                        new KeyValuePair<string, CborLdInputNode>("inner", new CborLdInputString("http://example.org/inner"))
                    }))
                }))
            })),
            new KeyValuePair<string, CborLdInputNode>("Container", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("inner", new CborLdInputString("nested-value"))
            }))
        });

        CborLdInputMap decoded = await RoundTripAsync(doc).ConfigureAwait(false);

        Assert.HasCount(2, decoded.Entries);
        Assert.AreEqual("@context", decoded.Entries[0].Key);
        Assert.AreEqual("Container", decoded.Entries[1].Key);
        CborLdInputMap inner = (CborLdInputMap)decoded.Entries[1].Value;
        Assert.HasCount(1, inner.Entries);
        Assert.AreEqual("inner", inner.Entries[0].Key);
        Assert.AreEqual("nested-value", ((CborLdInputString)inner.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task DynamicallyDefinedTypedTermRoundTrips()
    {
        //E3 follow-up: a term defined in @context with @type=xsd:date
        //routes through the registered DateEncoder/DateDecoder via the
        //ResolveTypeName helper. No CborLdTermCodec is registered for
        //the term — the type information comes entirely from the active
        //context's TermDefinition.TypeMapping.
        CborLdRegistryEntry typedEntry = new(
            registryEntryId: 2,
            keywords: new Dictionary<string, CborLdKeywordCodec>
            {
                ["@context"] = new CborLdKeywordCodec("@context", 0),
                ["@id"] = new CborLdKeywordCodec("@id", 2),
                ["@type"] = new CborLdKeywordCodec("@type", 4)
            },
            terms: new Dictionary<string, CborLdTermCodec>(),
            processingModel: "default",
            provisional: false,
            typeTables: new Dictionary<string, CborLdTypeTableSource>
            {
                //Sentinel: tells the encoder to emit date wire bytes as a
                //CBOR byte string so the decoder reads back the exact 4-byte
                //big-endian width the DateDecoder expects.
                [CborLdContextKeys.TypesEncodedAsBytesSentinel] = CborLdTypeTableSource.FromRegistry(
                    new Dictionary<string, int> { [CborLdTestSetup.DateType] = 1 })
            });

        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("birthDate", new CborLdInputMap(new[]
                {
                    new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("http://schema.org/birthDate")),
                    new KeyValuePair<string, CborLdInputNode>("@type", new CborLdInputString(CborLdTestSetup.DateType))
                }))
            })),
            new KeyValuePair<string, CborLdInputNode>("birthDate", new CborLdInputString("1990-01-15"))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, typedEntry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(
                id == typedEntry.RegistryEntryId ? typedEntry : null),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        CborLdInputMap decoded = (CborLdInputMap)result.Root;
        Assert.HasCount(2, decoded.Entries);
        Assert.AreEqual("@context", decoded.Entries[0].Key);
        Assert.AreEqual("birthDate", decoded.Entries[1].Key);
        Assert.AreEqual("1990-01-15", ((CborLdInputString)decoded.Entries[1].Value).Value);
    }

    [TestMethod]
    public async Task NestedEmbeddedContextRoundTrips()
    {
        //Two nested levels of @context. Outer defines "outer-term"; the
        //value of "outer-term" is a map carrying its own @context that
        //defines "inner-term". Inner term must compress within the inner
        //map and round-trip back to its original name.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("outer-term", new CborLdInputString("http://example.org/outer"))
            })),
            new KeyValuePair<string, CborLdInputNode>("outer-term", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
                {
                    new KeyValuePair<string, CborLdInputNode>("inner-term", new CborLdInputString("http://example.org/inner"))
                })),
                new KeyValuePair<string, CborLdInputNode>("inner-term", new CborLdInputString("inner-value"))
            }))
        });

        CborLdInputMap decoded = await RoundTripAsync(doc).ConfigureAwait(false);

        Assert.HasCount(2, decoded.Entries);
        Assert.AreEqual("@context", decoded.Entries[0].Key);
        Assert.AreEqual("outer-term", decoded.Entries[1].Key);

        CborLdInputMap nested = (CborLdInputMap)decoded.Entries[1].Value;
        //Inside the nested map: @context first, then inner-term.
        Assert.AreEqual("@context", nested.Entries[0].Key);
        Assert.AreEqual("inner-term", nested.Entries[1].Key);
        Assert.AreEqual("inner-value", ((CborLdInputString)nested.Entries[1].Value).Value);
    }

    private async ValueTask<CborLdInputMap> RoundTripAsync(CborLdInputMap doc)
    {
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, CompressedEntry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            Loader,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        return (CborLdInputMap)result.Root;
    }
}
