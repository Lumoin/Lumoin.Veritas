using System;
using System.Buffers;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.Drisl;
using Lumoin.Veritas.Cid;
using CidValue = Lumoin.Veritas.Cid.Cid;
using CidFormatter = Lumoin.Veritas.Cid.CidFormatter;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the throughput and allocation profile of
/// <see cref="CidCborConverter.Read"/>. The CID converter sits in the
/// hot path of every CAR-file parse and every DAG-CBOR document that
/// carries links; per-call overhead compounds across thousands of CIDs
/// in a typical AT Protocol repository snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is a buffer of <see cref="CidCount"/> CBOR-Tag-42 byte
/// strings concatenated, built once per run. Each iteration reads the
/// full buffer through a single <see cref="CborReader"/> instance.
/// </para>
/// <para>
/// What this measures: the rent/return pair through
/// <see cref="CborReader.ReadByteStringPooled"/>, the constant-byte
/// validation in <see cref="CidCborConverter"/>, and the
/// <c>CidParser.Parse</c> walk over the 36-byte CID body. What this
/// does <i>not</i> measure: CAR framing, DAG-CBOR enclosing-structure
/// walking, or actual byte-string content-segment crossings (the buffer
/// is single-segment).
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class CidConverterBenchmarks
{
    private const int CidCount = 1000;
    private byte[] buffer = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        //Build CidCount synthetic CIDs each encoded as a CBOR Tag 42
        //byte string whose 37-byte content is 0x00 (multibase prefix)
        //followed by version=1, codec=raw, sha-256 multihash of a
        //deterministic per-index input.
        ArrayBufferWriter<byte> sink = new();
        CborWriter writer = new(sink, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Span<byte> hash = stackalloc byte[32];
        Span<byte> counter = stackalloc byte[4];
        for(int i = 0; i < CidCount; i++)
        {
            //Hash a 4-byte counter to get a deterministic per-CID digest.
            counter[0] = (byte)i;
            counter[1] = (byte)(i >> 8);
            counter[2] = (byte)(i >> 16);
            counter[3] = (byte)(i >> 24);
            SHA256.HashData(counter, hash);

            CidValue cid = new()
            {
                Codec = CidCodec.Raw,
                Digest = Digest32.FromSpan(hash)
            };
            byte[] cidBytes = CidFormatter.ToBytes(cid);
            byte[] content = new byte[37];
            content[0] = 0x00;
            cidBytes.CopyTo(content, 1);

            writer.WriteTag(new CborTag(42));
            writer.WriteByteString(content);
        }

        buffer = sink.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Reads all <see cref="CidCount"/> CIDs from the prebuilt buffer.
    /// One iteration ≈ one CAR-file's worth of CID-converter calls.
    /// </summary>
    [Benchmark]
    public int ReadAllCids()
    {
        CborReader reader = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        CidCborConverter converter = new();
        int count = 0;
        for(int i = 0; i < CidCount; i++)
        {
            _ = converter.Read(reader);
            count++;
        }

        return count;
    }
}
