using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.Car;
using Lumoin.Veritas.Cbor.DagCbor;
using Lumoin.Veritas.Cbor.Drisl;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Long-running soak target for sampling profilers (dotnet-trace,
/// PerfView, etc.). Not a <see cref="BenchmarkDotNet.Attributes.BenchmarkAttribute"/>-
/// driven benchmark: runs as a plain method in the host process so a
/// profiler attached via PID gets clean call-stack samples without
/// BenchmarkDotNet's iteration / fork orchestration in the way.
/// </summary>
/// <remarks>
/// Usage from the repo root:
/// <code>
/// dotnet build test/Lumoin.Veritas.Benchmarks -c Release
/// dotnet dotnet-trace collect --format Speedscope --output cid-soak.nettrace -- ^
///   dotnet test/Lumoin.Veritas.Benchmarks/bin/Release/net10.0/Lumoin.Veritas.Benchmarks.dll --profile-cid
/// </code>
/// The Speedscope output is a JSON file ingestible by
/// <see href="https://www.speedscope.app"/> (drop-in browser viewer).
/// </remarks>
internal static class CarParseSoak
{
    public static void RunCidSoak(TimeSpan duration)
    {
        //Reuse the benchmark's corpus builder so the profile reflects
        //the same wire shape the BDN run measures.
        CidConverterBenchmarks corpusBuilder = new();
        corpusBuilder.GlobalSetup();
        Console.WriteLine($"[soak] CID corpus built; running for {duration.TotalSeconds:F0}s");

        long iterations = 0;
        Stopwatch sw = Stopwatch.StartNew();
        while(sw.Elapsed < duration)
        {
            corpusBuilder.ReadAllCids();
            iterations++;
        }
        sw.Stop();

        Console.WriteLine($"[soak] {iterations:N0} iterations in {sw.Elapsed.TotalSeconds:F2}s " +
            $"= {iterations / sw.Elapsed.TotalSeconds:N0} iter/s " +
            $"= {iterations * 1000 / sw.Elapsed.TotalSeconds:N0} CIDs/s");
    }

    public static void RunCarBlockSoak(TimeSpan duration)
    {
        //Load the real AT Protocol repository snapshot from the parser test
        //project's source tree (committed alongside that test project so the
        //8.9 MB blob is not duplicated into the benchmarks output).
        string fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Lumoin.Veritas.ParserTests", "Cbor", "Fixtures", "AtProto", "atproto-com.car"));
        if(!File.Exists(fixturePath))
        {
            Console.WriteLine($"[soak] fixture not found at {fixturePath}; cannot run CAR soak.");
            return;
        }

        byte[] carBytes = File.ReadAllBytes(fixturePath);
        Console.WriteLine($"[soak] CAR fixture: {carBytes.Length:N0} bytes; running for {duration.TotalSeconds:F0}s");

        //Warm up once outside the timed loop so JIT compilation cost is
        //not attributed to the first iteration's call stacks.
        (int warmupSections, int warmupBlocks) = ParseOnce(carBytes);
        Console.WriteLine($"[soak] warmup: {warmupSections:N0} sections, {warmupBlocks:N0} top-level CBOR items per pass");

        long iterations = 0;
        long sectionsParsed = 0;
        long blocksParsed = 0;
        Stopwatch sw = Stopwatch.StartNew();
        while(sw.Elapsed < duration)
        {
            (int sections, int blocks) = ParseOnce(carBytes);
            sectionsParsed += sections;
            blocksParsed += blocks;
            iterations++;
        }
        sw.Stop();

        Console.WriteLine($"[soak] {iterations:N0} full-CAR passes in {sw.Elapsed.TotalSeconds:F2}s " +
            $"= {iterations / sw.Elapsed.TotalSeconds:F2} passes/s; " +
            $"{sectionsParsed / iterations:N0} sections/pass; " +
            $"{blocksParsed:N0} top-level CBOR items total");
    }

    /// <summary>
    /// One full CARv1 parse pass over <paramref name="carBytes"/>. Reads
    /// the header, iterates every section (CID + block bytes), and walks
    /// every top-level DAG-CBOR item inside each block so the profile
    /// reflects the real cost of consuming an AT Protocol repository snapshot
    /// from start to end.
    /// </summary>
    private static (int sections, int blocks) ParseOnce(byte[] carBytes)
    {
        CarFileReader car = new(carBytes);
        CarFileHeader header = car.ReadHeader();
        _ = header.Roots.Count;

        int sections = 0;
        int blocks = 0;
        while(car.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            sections++;
            //Walk the block's DAG-CBOR content. This exercises the real
            //read path consumers hit when materialising records (posts,
            //profiles, ops, MST nodes, etc.).
            DagCborReader reader = new(block, strict: false);
            ConsumeValue(reader);
            blocks++;
        }

        return (sections, blocks);
    }

    /// <summary>
    /// As <see cref="RunCarBlockSoak"/>, but with a shared
    /// <see cref="CborStringInternPool"/> threaded into every
    /// <see cref="DagCborReader"/>. Repeated AT Protocol record-field
    /// keys ($type, cid, did, text, createdAt, etc.) are cached
    /// after first sighting, eliminating UTF-8 decode + string allocation
    /// on the hot path.
    /// </summary>
    public static void RunCarBlockSoakInterned(TimeSpan duration)
    {
        byte[]? carBytes = LoadFixture();
        if(carBytes is null)
        {
            return;
        }

        //Sized for an AT Protocol repository snapshot: enough room for every unique
        //short string (DIDs, dates, $type values, language tags). The
        //48-byte per-entry cap still filters out long unique content
        //(CIDs as text, AT URIs) from polluting the pool.
        CborStringInternPool pool = new(maxByteLength: 48, maxEntries: 65_536);
        //Pre-seed common AT Protocol record-field keys so the pool starts
        //warm. Not strictly required — cold start would converge to the
        //same hit rate within a few records — but eliminates the first-
        //sighting decode for the common keys, which biases the early
        //samples toward what steady-state looks like.
        string[] commonKeys =
        [
            "$type", "cid", "did", "rev", "data", "ops", "tid",
            "p", "ref", "version", "subject", "createdAt", "text",
            "type", "value", "blocks", "commit", "blobs", "since",
            "l", "k", "v", "t", "e", "displayName", "description"
        ];
        foreach(string key in commonKeys)
        {
            pool.Preseed(key);
        }

        Console.WriteLine($"[soak] CAR fixture: {carBytes.Length:N0} bytes (interned, {pool.Count} preseeded keys); running for {duration.TotalSeconds:F0}s");

        (int warmupSections, int warmupBlocks) = ParseOnceInterned(carBytes, pool);
        Console.WriteLine($"[soak] warmup: {warmupSections:N0} sections, {warmupBlocks:N0} top-level CBOR items per pass, pool now has {pool.Count} entries");

        long iterations = 0;
        long sectionsParsed = 0;
        long blocksParsed = 0;
        Stopwatch sw = Stopwatch.StartNew();
        while(sw.Elapsed < duration)
        {
            (int sections, int blocks) = ParseOnceInterned(carBytes, pool);
            sectionsParsed += sections;
            blocksParsed += blocks;
            iterations++;
        }
        sw.Stop();

        Console.WriteLine($"[soak/interned] {iterations:N0} full-CAR passes in {sw.Elapsed.TotalSeconds:F2}s " +
            $"= {iterations / sw.Elapsed.TotalSeconds:F2} passes/s; " +
            $"final pool size {pool.Count}");
    }

    /// <summary>
    /// As <see cref="RunCarBlockSoakInterned"/>, but additionally reuses
    /// a single <see cref="DagCborReader"/> instance across all sections
    /// via <see cref="DagCborReader.Reset(ReadOnlyMemory{byte})"/>.
    /// Eliminates per-section reader allocation; in combination with
    /// interning this is the steady-state shape for a long-lived
    /// firehose consumer.
    /// </summary>
    public static void RunCarBlockSoakReuse(TimeSpan duration)
    {
        byte[]? carBytes = LoadFixture();
        if(carBytes is null)
        {
            return;
        }

        CborStringInternPool pool = new(maxByteLength: 48, maxEntries: 65_536);
        string[] commonKeys =
        [
            "$type", "cid", "did", "rev", "data", "ops", "tid",
            "p", "ref", "version", "subject", "createdAt", "text",
            "type", "value", "blocks", "commit", "blobs", "since",
            "l", "k", "v", "t", "e", "displayName", "description"
        ];
        foreach(string key in commonKeys)
        {
            pool.Preseed(key);
        }

        Console.WriteLine($"[soak] CAR fixture: {carBytes.Length:N0} bytes (interned + reader reuse); running for {duration.TotalSeconds:F0}s");

        //Reused readers, one per loop. The DAG-CBOR reader's Reset
        //clears its frame stack and the inner CborReader's stack, so
        //each block parses cleanly.
        ReadOnlyMemory<byte> emptyMem = ReadOnlyMemory<byte>.Empty;
        DagCborReader reusedDagReader = new(emptyMem, strict: false, stringInternPool: pool);

        (int warmupSections, int warmupBlocks) = ParseOnceReuse(carBytes, reusedDagReader);
        Console.WriteLine($"[soak] warmup: {warmupSections:N0} sections, {warmupBlocks:N0} top-level CBOR items per pass, pool now has {pool.Count} entries");

        long iterations = 0;
        long sectionsParsed = 0;
        long blocksParsed = 0;
        Stopwatch sw = Stopwatch.StartNew();
        while(sw.Elapsed < duration)
        {
            (int sections, int blocks) = ParseOnceReuse(carBytes, reusedDagReader);
            sectionsParsed += sections;
            blocksParsed += blocks;
            iterations++;
        }
        sw.Stop();

        Console.WriteLine($"[soak/reuse] {iterations:N0} full-CAR passes in {sw.Elapsed.TotalSeconds:F2}s " +
            $"= {iterations / sw.Elapsed.TotalSeconds:F2} passes/s; " +
            $"final pool size {pool.Count}");
    }

    private static (int sections, int blocks) ParseOnceInterned(byte[] carBytes, CborStringInternPool pool)
    {
        CarFileReader car = new(carBytes);
        CarFileHeader header = car.ReadHeader();
        _ = header.Roots.Count;

        int sections = 0;
        int blocks = 0;
        while(car.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            sections++;
            DagCborReader reader = new(block, strict: false, stringInternPool: pool);
            ConsumeValue(reader);
            blocks++;
        }

        return (sections, blocks);
    }

    private static (int sections, int blocks) ParseOnceReuse(byte[] carBytes, DagCborReader reusedReader)
    {
        CarFileReader car = new(carBytes);
        CarFileHeader header = car.ReadHeader();
        _ = header.Roots.Count;

        int sections = 0;
        int blocks = 0;
        while(car.TryReadSection(out CidValue _, out ReadOnlyMemory<byte> block))
        {
            sections++;
            reusedReader.Reset(block);
            ConsumeValue(reusedReader);
            blocks++;
        }

        return (sections, blocks);
    }

    private static byte[]? LoadFixture()
    {
        string fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Lumoin.Veritas.ParserTests", "Cbor", "Fixtures", "AtProto", "atproto-com.car"));
        if(!File.Exists(fixturePath))
        {
            Console.WriteLine($"[soak] fixture not found at {fixturePath}; cannot run CAR soak.");
            return null;
        }

        return File.ReadAllBytes(fixturePath);
    }

    /// <summary>
    /// Recursive walker over a DAG-CBOR block. Recursion depth is
    /// bounded by AT Protocol record depth (~10 levels) so the call stack
    /// is safe even at firehose rates.
    /// </summary>
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

    private static void SkipOne(CborReader reader)
    {
        //Minimal skip: dispatch on state and consume one data item.
        //We don't recurse; non-container items are one call. Containers
        //are skipped via consuming all their children.
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
                _ = reader.ReadByteString();
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
            case CborReaderState.HalfPrecisionFloat:
            {
                _ = reader.ReadHalf();
                break;
            }
            case CborReaderState.SinglePrecisionFloat:
            {
                _ = reader.ReadSingle();
                break;
            }
            case CborReaderState.DoublePrecisionFloat:
            {
                _ = reader.ReadDouble();
                break;
            }
            case CborReaderState.StartArray:
            {
                int count = reader.ReadStartArray() ?? 0;
                for(int i = 0; i < count; i++)
                {
                    SkipOne(reader);
                }
                reader.ReadEndArray();
                break;
            }
            case CborReaderState.StartMap:
            {
                int count = reader.ReadStartMap() ?? 0;
                for(int i = 0; i < count; i++)
                {
                    SkipOne(reader);
                    SkipOne(reader);
                }
                reader.ReadEndMap();
                break;
            }
            default:
            {
                throw new InvalidOperationException($"Unhandled state in skip: {state}");
            }
        }
    }
}
