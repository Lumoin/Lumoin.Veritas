using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// Round-trip CBOR-LD documents through a <see cref="System.IO.Pipelines.Pipe"/>
/// as a transport. Complements the in-process streaming tests by exercising
/// the encode → pipe → decode path that downstream consumers use when
/// piping bytes across a Kestrel response, gRPC stream, or similar
/// byte-conveyor.
/// </summary>
/// <remarks>
/// These tests do not exercise multi-segment streaming during the decode
/// step itself — <see cref="CborLdDecoder"/> accepts a contiguous
/// <see cref="ReadOnlyMemory{T}"/>, so the pipe is drained to a single
/// buffer before decoding. The underlying <see cref="Cbor.CborReader"/>
/// already supports <see cref="ReadOnlySequence{T}"/> input directly; the
/// CBOR-LD layer's contiguous-memory API is a pragmatic boundary that
/// keeps the decoder allocation profile predictable.
/// </remarks>
[TestClass]
internal sealed class CborLdPipelineRoundTripTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task RoundTripsScalarThroughPipe()
    {
        CborLdInputNode root = new CborLdInputInt(42);

        CborLdInputNode decoded = await EncodeThenDecodeThroughPipeAsync(
            root, CborLdRegistryEntry.Passthrough).ConfigureAwait(false);

        Assert.IsInstanceOfType<CborLdInputInt>(decoded);
        Assert.AreEqual(42L, ((CborLdInputInt)decoded).Value);
    }

    [TestMethod]
    public async Task RoundTripsNestedMapThroughPipe()
    {
        //Mixed document with strings, integers, arrays, and nested maps —
        //covers the encoder/decoder type lattice through the pipe.
        CborLdInputMap root = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("@id", new CborLdInputString("urn:example:1")),
            new KeyValuePair<string, CborLdInputNode>("name", new CborLdInputString("Alice")),
            new KeyValuePair<string, CborLdInputNode>("count", new CborLdInputInt(7)),
            new KeyValuePair<string, CborLdInputNode>("tags", new CborLdInputArray(new CborLdInputNode[]
            {
                new CborLdInputString("alpha"),
                new CborLdInputString("beta")
            })),
            new KeyValuePair<string, CborLdInputNode>("nested", new CborLdInputMap(new[]
            {
                new KeyValuePair<string, CborLdInputNode>("flag", new CborLdInputBool(true))
            }))
        });

        CborLdInputNode decodedRoot = await EncodeThenDecodeThroughPipeAsync(
            root, CborLdRegistryEntry.Passthrough).ConfigureAwait(false);

        CborLdInputMap decoded = (CborLdInputMap)decodedRoot;
        Assert.HasCount(5, decoded.Entries);
        Assert.AreEqual("Alice", ((CborLdInputString)decoded.Entries[1].Value).Value);
        CborLdInputArray tags = (CborLdInputArray)decoded.Entries[3].Value;
        Assert.HasCount(2, tags.Items);
        CborLdInputMap nested = (CborLdInputMap)decoded.Entries[4].Value;
        Assert.IsTrue(((CborLdInputBool)nested.Entries[0].Value).Value);
    }

    [TestMethod]
    public async Task RoundTripsLargePayloadThroughPipe()
    {
        //Large enough to exercise pipe segment boundaries during the
        //drain. Verifies that no byte is lost in the segment-walking
        //read loop.
        CborLdInputMap root = new(new[]
        {
            new KeyValuePair<string, CborLdInputNode>("payload", new CborLdInputString(new string('x', 4096)))
        });

        CborLdInputNode decoded = await EncodeThenDecodeThroughPipeAsync(
            root, CborLdRegistryEntry.Passthrough).ConfigureAwait(false);

        CborLdInputMap decodedMap = (CborLdInputMap)decoded;
        Assert.HasCount(1, decodedMap.Entries);
        Assert.AreEqual(4096, ((CborLdInputString)decodedMap.Entries[0].Value).Value.Length);
    }

    /// <summary>
    /// Encodes <paramref name="root"/> into a fresh <see cref="Pipe"/>,
    /// reads the pipe's output side back to a contiguous buffer, and
    /// decodes that buffer through <see cref="CborLdDecoder.DecodeAsync"/>.
    /// </summary>
    private async ValueTask<CborLdInputNode> EncodeThenDecodeThroughPipeAsync(
        CborLdInputNode root,
        CborLdRegistryEntry entry)
    {
        //PipeOptions.Default thresholds (65536 / 32768) are well above
        //any test document size here, so FlushAsync never blocks. A
        //backpressure test would need concurrent producer/consumer tasks
        //because CborLdEncoder buffers its writes until flush; that adds
        //setup noise without strengthening this transport-sanity test.
        Pipe pipe = new(PipeOptions.Default);

        //Encoder writes to the pipe's writer end. CborLdEncoder accepts an
        //IBufferWriter<byte>; PipeWriter satisfies that interface.
        await CborLdEncoder.EncodeAsync(root, entry, CborLdProfile.Default, pipe.Writer).ConfigureAwait(false);
        await pipe.Writer.FlushAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await pipe.Writer.CompleteAsync().ConfigureAwait(false);

        //Drain the pipe's reader side into a contiguous buffer; CBOR-LD
        //decoder takes ReadOnlyMemory<byte>.
        ArrayBufferWriter<byte> drain = new();
        while(true)
        {
            ReadResult result = await pipe.Reader.ReadAsync(TestContext.CancellationToken).ConfigureAwait(false);
            foreach(ReadOnlyMemory<byte> segment in result.Buffer)
            {
                drain.Write(segment.Span);
            }

            pipe.Reader.AdvanceTo(result.Buffer.End);

            if(result.IsCompleted)
            {
                break;
            }
        }

        await pipe.Reader.CompleteAsync().ConfigureAwait(false);

        CborLdDecodeResult result2 = await CborLdDecoder.DecodeAsync(
            drain.WrittenMemory,
            (id, ct) => ValueTask.FromResult<CborLdRegistryEntry?>(id == entry.RegistryEntryId ? entry : null),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        return result2.Root;
    }
}
