using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.Car;
using Lumoin.Veritas.Cbor.DagCbor;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Compares the three CAR-parse configurations the soak measures:
/// (a) fresh <see cref="DagCborReader"/> per block, no intern pool;
/// (b) fresh reader per block plus a shared
/// <see cref="CborStringInternPool"/>; and (c) a single reused
/// <see cref="DagCborReader"/> with intern pool, reset between blocks.
/// The benchmark surfaces the allocation profile each configuration
/// produces in addition to wall-clock throughput.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(InProcessConfig))]
public class CarParseBenchmarks
{
    private byte[] carBytes = null!;
    private CborStringInternPool sharedPool = null!;
    private DagCborReader reusedReader = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        string fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Lumoin.Veritas.ParserTests", "Cbor", "Fixtures", "AtProto", "atproto-com.car"));
        if(!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                $"CAR fixture is not present at {fixturePath}; populate Fixtures/AtProto/ before benchmarking.",
                fixturePath);
        }
        carBytes = File.ReadAllBytes(fixturePath);

        sharedPool = new CborStringInternPool(maxByteLength: 48, maxEntries: 65_536);
        foreach(string key in new[] { "$type", "cid", "did", "rev", "data", "ops", "tid",
            "p", "ref", "version", "subject", "createdAt", "text",
            "type", "value", "blocks", "commit", "blobs", "since" })
        {
            sharedPool.Preseed(key);
        }

        //Pre-warm the pool by running one pass under the interned config.
        ParseCarBlocksInterned();

        reusedReader = new DagCborReader(ReadOnlyMemory<byte>.Empty, strict: false, stringInternPool: sharedPool);
    }

    [Benchmark(Baseline = true)]
    public int ParseCarBlocksBaseline()
    {
        CarFileReader car = new(carBytes);
        car.ReadHeader();
        int blocks = 0;
        while(car.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            DagCborReader reader = new(block, strict: false);
            ConsumeValue(reader);
            blocks++;
        }

        return blocks;
    }

    [Benchmark]
    public int ParseCarBlocksInterned()
    {
        CarFileReader car = new(carBytes);
        car.ReadHeader();
        int blocks = 0;
        while(car.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            DagCborReader reader = new(block, strict: false, stringInternPool: sharedPool);
            ConsumeValue(reader);
            blocks++;
        }

        return blocks;
    }

    [Benchmark]
    public int ParseCarBlocksReuse()
    {
        CarFileReader car = new(carBytes);
        car.ReadHeader();
        int blocks = 0;
        while(car.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            reusedReader.Reset(block);
            ConsumeValue(reusedReader);
            blocks++;
        }

        return blocks;
    }

    private sealed class InProcessConfig: ManualConfig
    {
        public InProcessConfig()
        {
            AddJob(Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance));
        }
    }

    private static void ConsumeValue(DagCborReader reader)
    {
        CborReaderState state = reader.PeekState();
        switch(state)
        {
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
            {
                _ = reader.ReadInt64();
                break;
            }
            case CborReaderState.ByteString:
            {
                _ = reader.ReadByteStringSpan();
                break;
            }
            case CborReaderState.TextString:
            {
                _ = reader.ReadTextString();
                break;
            }
            case CborReaderState.Boolean:
            {
                _ = reader.ReadBoolean();
                break;
            }
            case CborReaderState.Null:
            {
                reader.ReadNull();
                break;
            }
            case CborReaderState.DoublePrecisionFloat:
            case CborReaderState.SinglePrecisionFloat:
            case CborReaderState.HalfPrecisionFloat:
            {
                _ = reader.ReadDouble();
                break;
            }
            case CborReaderState.Tag:
            {
                _ = reader.ReadCid();
                break;
            }
            case CborReaderState.StartArray:
            {
                int count = reader.ReadStartArray();
                for(int i = 0; i < count; i++)
                {
                    ConsumeValue(reader);
                }
                reader.ReadEndArray();
                break;
            }
            case CborReaderState.StartMap:
            {
                int count = reader.ReadStartMap();
                for(int i = 0; i < count; i++)
                {
                    _ = reader.ReadTextString();
                    ConsumeValue(reader);
                }
                reader.ReadEndMap();
                break;
            }
            default:
            {
                return;
            }
        }
    }
}
