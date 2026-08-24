using System.Buffers;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Cbor.Drisl;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the encoding throughput of <see cref="DrislWriter"/> on a
/// representative DRISL-shaped object: a map with text-string keys whose
/// values include integers, byte strings, and CID-tagged byte strings.
/// </summary>
/// <remarks>
/// The benchmark exercises the path most relevant to consumers: a
/// document with sorted text-string-keyed maps, finite numeric values,
/// and a CID under Tag 42 with the multibase prefix. The output is a
/// canonical-form CBOR-LD-compatible payload suitable for signing.
/// </remarks>
[MemoryDiagnoser]
public class DrislBenchmarks
{
    private global::Lumoin.Veritas.Cid.Cid sampleCid = null!;
    private byte[] payload = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        HashDelegate sha256 = SHA256.HashData;
        sampleCid = global::Lumoin.Veritas.Cid.CidHasher.ComputeFromBytes(
            "benchmark"u8,
            global::Lumoin.Veritas.Cid.CidCodec.Raw,
            sha256);
        payload = new byte[64];
        new System.Random(7).NextBytes(payload);
    }

    [Benchmark]
    public int EncodeDocumentWithCidAndPayload()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);
        writer.WriteStartMap(3);
        writer.WriteTextString("cid");
        writer.WriteCid(sampleCid);
        writer.WriteTextString("payload");
        writer.WriteByteString(payload);
        writer.WriteTextString("count");
        writer.WriteInt64(42);
        writer.WriteEndMap();
        return writer.BytesWritten;
    }
}
