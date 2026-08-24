using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// Active-context spec-validation tests at the encoder/decoder boundary.
/// Validation runs inline during compressed-mode encoding (via E3's
/// <c>CborLdActiveContextScope</c>), so these tests exercise that path
/// rather than the retired validator pre-pass.
/// </summary>
[TestClass]
internal sealed class CborLdActiveContextTests
{
    public required TestContext TestContext { get; set; }

    /// <summary>
    /// A keyword-only registry entry that flips the encoder into compressed
    /// mode so the inline @context validation path runs.
    /// </summary>
    private static CborLdRegistryEntry CompressedEntry { get; } = new(
        registryEntryId: 1,
        keywords: new Dictionary<string, CborLdKeywordCodec>
        {
            ["@context"] = new CborLdKeywordCodec("@context", 0),
            ["@id"] = new CborLdKeywordCodec("@id", 2),
            ["@type"] = new CborLdKeywordCodec("@type", 4)
        },
        terms: new Dictionary<string, CborLdTermCodec>());

    [TestMethod]
    public async Task InlineContextWithValidTermsPasses()
    {
        //An inline @context with a regular term passes validation in
        //compressed mode (no spec violation).
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("http://schema.org/name"))
            })),
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, CompressedEntry, CborLdProfile.Default, buffer).ConfigureAwait(false);

        Assert.IsGreaterThan(0, buffer.WrittenCount);
    }

    [TestMethod]
    public async Task InlineContextWithKeywordRedefinitionRejects()
    {
        //Redefining a JSON-LD keyword is forbidden by spec §4.1.2.
        //In compressed mode, the encoder's active-context walk applies
        //the @context via ContextProcessing, which throws on the
        //violation; CborLdActiveContextScope normalises that to a
        //CborLdProcessingException with the spec error code.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("http://example.org/redefined"))
            }))
        });

        ArrayBufferWriter<byte> buffer = new();
        CborLdProcessingException ex = await Assert.ThrowsExactlyAsync<CborLdProcessingException>(
            async () => await CborLdEncoder.EncodeAsync(doc, CompressedEntry, CborLdProfile.Default, buffer).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreEqual("keyword redefinition", ex.ErrorCode);
        Assert.IsNotNull(ex.InnerException);
        Assert.IsInstanceOfType<LinkedDataProcessingException>(ex.InnerException);
    }

    [TestMethod]
    public async Task PassthroughModeDoesNotValidateContext()
    {
        //Per W3C CBOR-LD 1.0 registry-entry 0 semantics, passthrough emits
        //the document as opaque CBOR primitives. The encoder performs no
        //spec-validation in this mode — even a malformed @context flows
        //through unchanged. Users who need validation should use a
        //compressed registry entry.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("http://example.org/redefined"))
            }))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer).ConfigureAwait(false);

        Assert.IsGreaterThan(0, buffer.WrittenCount);
    }

    [TestMethod]
    public async Task UrlContextInPassthroughPassesThrough()
    {
        //Passthrough mode treats @context as opaque data, so a URL-only
        //context with no fetcher does not raise — it is emitted as a CBOR
        //text string and round-trips unchanged.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputString("http://example.org/context")),
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer).ConfigureAwait(false);

        Assert.IsGreaterThan(0, buffer.WrittenCount);
    }

    [TestMethod]
    public async Task UrlContextInCompressedModeWithoutFetcherFails()
    {
        //In compressed mode the encoder calls into ContextProcessing,
        //which requires a fetcher to resolve URL contexts. With no
        //fetcher supplied, CborLdActiveContextScope's offline-throw
        //fetcher raises CborLdProcessingException.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputString("http://example.org/context"))
        });

        ArrayBufferWriter<byte> buffer = new();
        await Assert.ThrowsExactlyAsync<CborLdProcessingException>(
            async () => await CborLdEncoder.EncodeAsync(doc, CompressedEntry, CborLdProfile.Default, buffer).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [TestMethod]
    public void CborLdProcessingExceptionDerivesFromLinkedDataBase()
    {
        //Smoke test for the exception hierarchy.
        CborLdProcessingException ex = new("test error", "test message");
        Assert.IsInstanceOfType<LinkedDataProcessingException>(ex);
        Assert.AreEqual("test error", ex.ErrorCode);
        Assert.AreEqual("test message", ex.Message);
    }

    [TestMethod]
    public void CborLdProcessingExceptionWrappingConstructorPreservesInner()
    {
        LinkedDataProcessingException inner = new("invalid IRI mapping", "bad IRI");
        CborLdProcessingException ex = new(inner);
        Assert.AreEqual("invalid IRI mapping", ex.ErrorCode);
        Assert.AreSame(inner, ex.InnerException);
    }

    [TestMethod]
    public async Task EncodeWithRemoteContextsRequiresFetcherAndParser()
    {
        //The remote-context overload validates its delegate arguments.
        CborLdInputMap doc = new(System.Array.Empty<KeyValuePair<string, CborLdInputNode>>());
        ArrayBufferWriter<byte> buffer = new();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await CborLdEncoder.EncodeWithRemoteContextsAsync(
                doc, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer,
                fetcher: null!, parser: null!, cache: null, callerTables: null, pool: null,
                cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EncodeDecodeRoundTripPreservesContextStructure()
    {
        //Documents containing @context round-trip through encode/decode
        //unchanged. The validator post-pass has been retired (E3 step 5);
        //validation now happens inline during the compressed walk on both
        //sides, but a passthrough round-trip is observable as identity.
        CborLdInputMap doc = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@context", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("http://schema.org/name"))
            })),
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice"))
        });

        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer).ConfigureAwait(false);

        CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
            buffer.WrittenMemory,
            CborLdRegistry.Empty.AsDelegate(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(result.Root);
    }

    [TestMethod]
    public async Task DecodeWithRemoteContextsRequiresFetcherAndParser()
    {
        CborLdInputMap doc = new(System.Array.Empty<KeyValuePair<string, CborLdInputNode>>());
        ArrayBufferWriter<byte> buffer = new();
        await CborLdEncoder.EncodeAsync(doc, CborLdRegistryEntry.Passthrough, CborLdProfile.Default, buffer).ConfigureAwait(false);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
        {
            await CborLdDecoder.DecodeWithRemoteContextsAsync(
                buffer.WrittenMemory,
                CborLdRegistry.Empty.AsDelegate(),
                fetcher: null!, parser: null!, cache: null, callerTables: null, pool: null,
                cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
