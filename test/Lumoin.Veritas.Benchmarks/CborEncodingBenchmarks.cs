using System.Buffers;
using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the encoding throughput of the project's <see cref="CborWriter"/>
/// on a fixed corpus of representative values, run once per
/// <see cref="ConformanceMode"/> combination. Comparison with the BCL
/// <see cref="System.Formats.Cbor.CborWriter"/> is exercised in the
/// differential tests; the benchmarks project is bound by the banned-API
/// rule that keeps BCL CBOR out of non-Cbor library code, so the
/// comparison lives there.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is built once per run in <see cref="GlobalSetup"/>, so its
/// allocation cost does not land inside the measured path. The
/// per-iteration buffer is freshly allocated to match how a streaming
/// consumer behaves; benchmarks that reuse a pooled buffer belong in a
/// separate class.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class CborEncodingBenchmarks
{
    private string[] keys = null!;
    private long[] values = null!;
    private byte[][] byteBlobs = null!;

    [Params(CborConformanceMode.Lax, CborConformanceMode.RfcCanonical, CborConformanceMode.Cde)]
    public CborConformanceMode Mode { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        keys = new string[100];
        values = new long[100];
        byteBlobs = new byte[10][];
        System.Random random = new(42);
        for(int i = 0; i < keys.Length; i++)
        {
            keys[i] = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"k{i:000}");
            values[i] = random.NextInt64(-1_000_000, 1_000_000);
        }
        for(int i = 0; i < byteBlobs.Length; i++)
        {
            byteBlobs[i] = new byte[32];
            random.NextBytes(byteBlobs[i]);
        }
    }

    [Benchmark]
    public int EncodeCorpus()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(Mode));
        writer.WriteStartMap(keys.Length);
        for(int i = 0; i < keys.Length; i++)
        {
            writer.WriteTextString(keys[i]);
            writer.WriteInt64(values[i]);
        }
        writer.WriteEndMap();
        writer.WriteStartArray(byteBlobs.Length);
        foreach(byte[] blob in byteBlobs)
        {
            writer.WriteByteString(blob);
        }
        writer.WriteEndArray();
        return writer.BytesWritten;
    }
}
