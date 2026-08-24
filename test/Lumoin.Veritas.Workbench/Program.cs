using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.NQuads;

namespace Lumoin.Veritas.Workbench;

/// <summary>
/// Console entrypoint for the Veritas developer workbench. Parses
/// argv, invokes the requested scenario in the same assembly, and
/// reports the result to <see cref="Console.Out"/>. Each scenario
/// runs synchronously on the main thread so a sampling profiler
/// attached to this process produces attributed call-stack samples
/// without thread-pool jumps.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage from the repo root</b> (always against a Release build):
/// <code>
/// dotnet run -c Release --project test/Lumoin.Veritas.Workbench -- --profile-build
/// dotnet run -c Release --project test/Lumoin.Veritas.Workbench -- --profile-query --duration 30
/// dotnet run -c Release --project test/Lumoin.Veritas.Workbench -- --profile-build --duration 60 --triple-count 1000000
/// dotnet run -c Release --project test/Lumoin.Veritas.Workbench -- --profile-edgemap-distribution path/to/data.nq
/// </code>
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>Default soak duration when <c>--duration</c> is not supplied.</summary>
    public const int DefaultDurationSeconds = 20;

    /// <summary>
    /// Entrypoint. Returns 0 on success, 1 on argument or
    /// invocation errors. Errors are written to
    /// <see cref="Console.Error"/>; results to
    /// <see cref="Console.Out"/>.
    /// </summary>
    /// <param name="args">Argument vector from the OS.</param>
    /// <returns>Exit code; 0 on success.</returns>
    public static async Task<int> Main(string[] args)
    {
        if(args.Length == 0 || IsHelpRequest(args[0]))
        {
            PrintUsage(Console.Out);

            return 0;
        }

        switch(args[0])
        {
            case "--profile-build":
            {
                if(!TryParseSoakOptions(args, out TimeSpan duration, out int tripleCount, out string? error))
                {
                    await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
                    PrintUsage(Console.Error);

                    return 1;
                }

                await Console.Out.WriteLineAsync($"[soak] hypertrie build, {tripleCount:N0} triples, target duration {duration.TotalSeconds:F0}s").ConfigureAwait(false);
                SoakResult result = await HypertrieSoak.RunBuildSoakAsync(duration, TimeProvider.System, tripleCount).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(FormatBuildResult(result, tripleCount)).ConfigureAwait(false);

                return 0;
            }
            case "--profile-query":
            {
                if(!TryParseSoakOptions(args, out TimeSpan duration, out int tripleCount, out string? error))
                {
                    await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
                    PrintUsage(Console.Error);

                    return 1;
                }

                await Console.Out.WriteLineAsync($"[soak] hypertrie query, {tripleCount:N0} triples, target duration {duration.TotalSeconds:F0}s").ConfigureAwait(false);
                SoakResult result = await HypertrieSoak.RunQuerySoakAsync(duration, TimeProvider.System, tripleCount).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(FormatQueryResult(result)).ConfigureAwait(false);

                return 0;
            }
            case "--profile-build-allocations":
            {
                if(!TryParseSoakOptions(args, out TimeSpan _, out int tripleCount, out string? error))
                {
                    await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
                    PrintUsage(Console.Error);

                    return 1;
                }

                await Console.Out.WriteLineAsync($"[alloc] hypertrie build, {tripleCount:N0} triples").ConfigureAwait(false);
                AllocationResult allocResult = await HypertrieSoak.RunBuildAllocationProbeAsync(tripleCount).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(FormatAllocationResult(allocResult, tripleCount)).ConfigureAwait(false);

                return 0;
            }
            case "--profile-edgemap-distribution":
            {
                if(args.Length < 2)
                {
                    await Console.Error.WriteLineAsync("--profile-edgemap-distribution requires a path argument or --synthetic [--triple-count N].").ConfigureAwait(false);
                    PrintUsage(Console.Error);

                    return 1;
                }

                if(args[1] == "--synthetic")
                {
                    if(!TryParseSyntheticTripleCount(args, out int syntheticTripleCount, out string? syntheticError))
                    {
                        await Console.Error.WriteLineAsync(syntheticError).ConfigureAwait(false);
                        PrintUsage(Console.Error);

                        return 1;
                    }

                    return await RunEdgeMapDistributionSyntheticAsync(syntheticTripleCount, CancellationToken.None).ConfigureAwait(false);
                }

                string corpusPath = args[1];
                if(!File.Exists(corpusPath))
                {
                    await Console.Error.WriteLineAsync($"File not found: {corpusPath}").ConfigureAwait(false);

                    return 1;
                }

                return await RunEdgeMapDistributionAsync(corpusPath, CancellationToken.None).ConfigureAwait(false);
            }
            default:
            {
                await Console.Error.WriteLineAsync($"Unknown command: {args[0]}").ConfigureAwait(false);
                PrintUsage(Console.Error);

                return 1;
            }
        }
    }

    private static bool IsHelpRequest(string arg)
    {
        return arg == "--help" || arg == "-h" || arg == "/?";
    }

    //Parses --duration and --triple-count flags from the soak-scenario
    //argv. Returns false when an unknown flag is present so the
    //caller surfaces the error to stderr and prints usage.
    private static bool TryParseSoakOptions(
        string[] args,
        out TimeSpan duration,
        out int tripleCount,
        out string? error)
    {
        duration = TimeSpan.FromSeconds(DefaultDurationSeconds);
        tripleCount = HypertrieSoak.DefaultTripleCount;
        error = null;

        for(int i = 1; i < args.Length; i++)
        {
            switch(args[i])
            {
                case "--duration":
                {
                    if(i + 1 >= args.Length)
                    {
                        error = "--duration requires a non-negative integer number of seconds.";

                        return false;
                    }

                    if(!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds < 0)
                    {
                        error = $"--duration value '{args[i + 1]}' is not a non-negative integer.";

                        return false;
                    }

                    duration = TimeSpan.FromSeconds(seconds);
                    i++;
                    break;
                }
                case "--triple-count":
                {
                    if(i + 1 >= args.Length)
                    {
                        error = "--triple-count requires a non-negative integer.";

                        return false;
                    }

                    if(!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                    {
                        error = $"--triple-count value '{args[i + 1]}' is not a non-negative integer.";

                        return false;
                    }

                    tripleCount = parsed;
                    i++;
                    break;
                }
                default:
                {
                    error = $"Unknown option: {args[i]}";

                    return false;
                }
            }
        }

        return true;
    }

    //Parses the optional --triple-count <N> tail of a --synthetic
    //EdgeMap-distribution invocation. Defaults to
    //HypertrieSoak.DefaultTripleCount when absent.
    private static bool TryParseSyntheticTripleCount(
        string[] args,
        out int tripleCount,
        out string? error)
    {
        tripleCount = HypertrieSoak.DefaultTripleCount;
        error = null;

        for(int i = 2; i < args.Length; i++)
        {
            switch(args[i])
            {
                case "--triple-count":
                {
                    if(i + 1 >= args.Length)
                    {
                        error = "--triple-count requires a non-negative integer.";

                        return false;
                    }

                    if(!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                    {
                        error = $"--triple-count value '{args[i + 1]}' is not a non-negative integer.";

                        return false;
                    }

                    tripleCount = parsed;
                    i++;
                    break;
                }
                default:
                {
                    error = $"Unknown option: {args[i]}";

                    return false;
                }
            }
        }

        return true;
    }

    //Generates a Seed=1 synthetic corpus matching HypertrieSoak's
    //RNG draws, builds the hypertrie, surveys EdgeMap distribution,
    //and prints the histogram. Mirrors the pre-Batch-4 standalone
    //EdgeMapSurvey program's input shape so the surveys are
    //comparable corpus-for-corpus.
    private static async Task<int> RunEdgeMapDistributionSyntheticAsync(int tripleCount, CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"[soak] edgemap distribution, synthetic Seed=1 corpus, target {tripleCount:N0} triples").ConfigureAwait(false);

        Stopwatch generateWatch = Stopwatch.StartNew();
        EncodedTriple[] triples = HypertrieSoak.GenerateSyntheticTriples(tripleCount);
        generateWatch.Stop();

        await Console.Out.WriteLineAsync($"[soak] generated {triples.Length:N0} distinct triples in {generateWatch.Elapsed.TotalSeconds:F2}s").ConfigureAwait(false);

        Stopwatch buildWatch = Stopwatch.StartNew();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        buildWatch.Stop();

        await Console.Out.WriteLineAsync($"[soak] built hypertrie in {buildWatch.Elapsed.TotalSeconds:F2}s").ConfigureAwait(false);

        EdgeMapDistributionResult result = EdgeMapDistribution.Survey(store.Snapshot.Store, store.Snapshot.Root);
        await Console.Out.WriteLineAsync(FormatDistributionResult(result)).ConfigureAwait(false);

        return 0;
    }

    //Loads the NQuads corpus, builds the hypertrie, surveys
    //EdgeMap distribution, and prints the histogram.
    private static async Task<int> RunEdgeMapDistributionAsync(string corpusPath, CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"[soak] edgemap distribution, corpus {corpusPath}").ConfigureAwait(false);

        TermDictionary dictionary = new();
        List<EncodedTriple> triples = [];

        FileStream stream = File.OpenRead(corpusPath);
        try
        {
            PipeReader pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
            await foreach(Quad quad in NQuadsReader.ReadAsync(pipe, pool: null, cancellationToken).ConfigureAwait(false))
            {
                TermId subject = dictionary.GetOrAdd(quad.Subject);
                TermId predicate = dictionary.GetOrAdd(quad.Predicate);
                TermId @object = dictionary.GetOrAdd(quad.Object);
                triples.Add(new EncodedTriple(subject, predicate, @object));
            }
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync($"[soak] loaded {triples.Count:N0} triples, {dictionary.Count:N0} distinct terms").ConfigureAwait(false);

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        EdgeMapDistributionResult result = EdgeMapDistribution.Survey(store.Snapshot.Store, store.Snapshot.Root);

        await Console.Out.WriteLineAsync(FormatDistributionResult(result)).ConfigureAwait(false);

        return 0;
    }

    private static string FormatBuildResult(SoakResult result, int tripleCount)
    {
        double secs = result.Elapsed.TotalSeconds;
        double iterPerSec = secs > 0 ? result.Iterations / secs : 0;
        double triplesPerSec = secs > 0 ? (double)tripleCount * result.Iterations / secs : 0;

        return $"[soak] {result.Iterations:N0} build iterations in {secs:F2}s " +
            $"= {iterPerSec:N0} iter/s ({triplesPerSec:N0} triples/s)";
    }

    private static string FormatAllocationResult(AllocationResult result, int tripleCount)
    {
        double secs = result.Elapsed.TotalSeconds;
        double mib = result.AllocatedBytes / (1024d * 1024d);
        double gib = mib / 1024d;
        double bytesPerTriple = tripleCount > 0 ? (double)result.AllocatedBytes / tripleCount : 0;

        return $"[alloc] build of {tripleCount:N0} triples took {secs:F2}s, " +
            $"allocated {result.AllocatedBytes:N0} bytes ({mib:F1} MiB, {gib:F2} GiB), " +
            $"~{bytesPerTriple:F1} bytes/triple, peak gen2 size {result.PeakGen2Bytes:N0} bytes";
    }

    private static string FormatQueryResult(SoakResult result)
    {
        double secs = result.Elapsed.TotalSeconds;
        double iterPerSec = secs > 0 ? result.Iterations / secs : 0;

        return $"[soak] {result.Iterations:N0} query iterations in {secs:F2}s " +
            $"= {iterPerSec:N0} iter/s ({result.AuxiliaryCount:N0} total solutions emitted)";
    }

    private static string FormatDistributionResult(EdgeMapDistributionResult result)
    {
        System.Text.StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"[soak] distinct nodes: {result.DistinctNodeCount:N0}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"[soak] total edgemaps: {result.TotalEdgeMaps:N0}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"[soak]   Empty:        {result.EmptyCount:N0}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"[soak]   Inline(1-8):  {result.InlineCount:N0}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"[soak]   SortedArray:  {result.SortedArrayCount:N0}");

        if(result.InlineCount > 0)
        {
            sb.AppendLine("[soak]   Inline histogram (entry count: edgemaps):");
            foreach(KeyValuePair<int, int> bucket in result.InlineCountHistogram)
            {
                string label = bucket.Key switch
                {
                    1 => "    1",
                    2 => "    2",
                    3 => "  3-4",
                    5 => "  5-8",
                    _ => $"{bucket.Key,5}",
                };
                sb.AppendLine(CultureInfo.InvariantCulture, $"[soak]     {label} : {bucket.Value:N0}");
            }
        }

        if(result.SortedArrayCount > 0)
        {
            sb.AppendLine("[soak]   SortedArray histogram (bucket lower-bound: count):");
            foreach(KeyValuePair<int, int> bucket in result.SortedArrayCountHistogram)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"[soak]     {bucket.Key,5}+ : {bucket.Value:N0}");
            }
        }

        if(result.PerDepthTierCounts.Count > 0)
        {
            sb.AppendLine("[soak]   Per-depth tier counts (depth: empty/inline/sortedArray):");
            List<int> depths = [.. result.PerDepthTierCounts.Keys];
            depths.Sort((a, b) => b.CompareTo(a));
            foreach(int depth in depths)
            {
                int[] counts = result.PerDepthTierCounts[depth];
                sb.AppendLine(CultureInfo.InvariantCulture, $"[soak]     depth {depth}: {counts[0]:N0} / {counts[1]:N0} / {counts[2]:N0}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Lumoin.Veritas.Workbench");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  workbench --profile-build [--duration <seconds>] [--triple-count <N>]");
        writer.WriteLine("  workbench --profile-query [--duration <seconds>] [--triple-count <N>]");
        writer.WriteLine("  workbench --profile-edgemap-distribution <path-to-nquads-file>");
        writer.WriteLine("  workbench --profile-edgemap-distribution --synthetic [--triple-count <N>]");
        writer.WriteLine("  workbench --help");
        writer.WriteLine();
        writer.WriteLine("Defaults:");
        writer.WriteLine($"  --duration       {DefaultDurationSeconds}");
        writer.WriteLine($"  --triple-count   {HypertrieSoak.DefaultTripleCount}");
    }
}
