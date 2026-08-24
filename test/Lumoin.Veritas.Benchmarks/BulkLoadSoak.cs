using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The bulk-load ingest soak: measures the streaming-ingest boundary against the list-materialising comparand at a
/// tunable scale, over both acceptance halves — file to served immutable engine (Half A) and file to persisted
/// mutable generation (Half B). Not a <see cref="BenchmarkDotNet.Attributes.BenchmarkAttribute"/>-driven benchmark:
/// each route runs in a single-purpose child process so <see cref="GC.GetTotalAllocatedBytes(bool)"/> and
/// <see cref="Process.PeakWorkingSet64"/>, both process-wide, are genuinely that route's figures. Run with
/// <c>dotnet run -c Release -- --profile-bulk-load --triples 1000000</c>.
/// </summary>
/// <remarks>
/// <para>
/// The streaming route drives the real N-Quads parser over a <see cref="PipeReader"/> straight into the engine's
/// quad-stream open (the same route the CLI loader wires), so each quad is encoded and discarded as it arrives — no
/// intermediate <see cref="Quad"/> or <see cref="DataTriple"/> list. The list route parses the whole file into
/// materialised lists first, then opens through the shared encoded-input core, so the streaming/list delta is exactly
/// the composition cost — the retained parse-object set the streaming route never holds. Half A's list route uses the
/// genuine default-plus-named list overload; Half B's mutable list route feeds a materialised <see cref="Quad"/> list
/// through the same quad-stream open (the mutable API has no named-graph list overload, so a retained list through the
/// one open isolates the composition cost most tightly — the only difference from streaming is that the list is held).
/// </para>
/// <para>
/// Per-route process-isolation contract: <see cref="Process.PeakWorkingSet64"/> is an un-resettable process-lifetime
/// high-water mark, so a single process measuring several routes in sequence attributes the largest earlier footprint
/// to every later route (and under server GC a freed heap's segments stay retained). The parent
/// <c>--profile-bulk-load</c> run therefore generates the corpus ONCE and spawns ONE CHILD PROCESS PER ROUTE (this
/// same executable with <c>--profile-bulk-load-route</c>); each child measures only its own route and prints one
/// machine-readable result line the parent parses into the report. Each child JIT-warms itself over a tiny
/// 256-triple corpus — one untimed same-route warm open plus the probe queries — before its timed repetitions; that
/// warm-up is included in the child's peak, which is why it is kept small: the measured route's footprint dominates
/// at the measured scales. The parent exits non-zero when any child fails.
/// </para>
/// </remarks>
internal static class BulkLoadSoak
{
    /// <summary>The IRI namespace every generated term shares.</summary>
    private const string Base = "http://veritas.example/";

    /// <summary>The size of the shared predicate vocabulary the chain edges cycle through.</summary>
    private const int PredicateCount = 4;

    /// <summary>The number of named graphs the generated corpus spreads its named triples across.</summary>
    private const int NamedGraphCount = 3;

    /// <summary>Every this-many-th triple is routed into a named graph; the rest form the default-graph chain.</summary>
    private const int NamedGraphEvery = 8;

    /// <summary>The default total triple count when <c>--triples</c> is not given.</summary>
    private const long DefaultTripleCount = 1_000_000;

    /// <summary>The default number of timed repetitions when <c>--repeats</c> is not given.</summary>
    private const int DefaultRepeats = 1;

    /// <summary>The tiny in-child warm-up corpus size: enough to JIT the route's code path, small enough that the warm-up's contribution to the child's process-lifetime peak stays negligible against the measured scale.</summary>
    private const long WarmupTripleCount = 256;

    /// <summary>The route name for the streaming open of a served immutable engine (Half A).</summary>
    private const string RouteServedStreaming = "servedStreaming";

    /// <summary>The route name for the list-materialising open of a served immutable engine (Half A comparand).</summary>
    private const string RouteServedList = "servedList";

    /// <summary>The route name for the streaming open and persist of a mutable engine (Half B).</summary>
    private const string RoutePersistedStreaming = "persistedStreaming";

    /// <summary>The route name for the list-materialising open and persist of a mutable engine (Half B comparand).</summary>
    private const string RoutePersistedList = "persistedList";

    /// <summary>The prefix of the one machine-readable result line a route child prints and the parent parses.</summary>
    private const string RouteLinePrefix = "[bulk-load-route] ";

    /// <summary>The closing statement of the peak working-set semantics, held as a value so it is not a literal argument to <see cref="Console.WriteLine(string)"/>.</summary>
    private static string RouteIsolationNote { get; } = "[bulk-load] note: every route ran in its own child process, so each peak working set is that route's own process-lifetime high-water mark; a child's peak includes its 256-triple warm-up, negligible at the measured scales.";

    /// <summary>The ingest configuration: reasoning is disabled so the measurement is the ingest composition alone (the mutable half never reasons; the immutable list and stream cores share the reasoning wiring, so it does not affect the delta) and the served triple counts stay exactly the ingested counts.</summary>
    private static VeritasEngineOptions IngestOptions { get; } = VeritasEngineOptions.Default with { Reasoning = null };

    /// <summary>
    /// Runs the bulk-load soak as the parent orchestrator: generates a deterministic N-Quads corpus once, spawns one
    /// isolated child process per route, parses each child's result line, and prints the per-route absolutes plus the
    /// streaming/list ratios for both halves. Sets a non-zero <see cref="Environment.ExitCode"/> when a child fails.
    /// </summary>
    /// <param name="args">The command-line arguments; <c>--triples N</c> (default 1,000,000) and <c>--repeats R</c> (default 1, clamped to at least 1) are honoured.</param>
    /// <returns>A task that completes when the soak has run and printed its report.</returns>
    public static async Task RunBulkLoadSoakAsync(string[] args)
    {
        long tripleCount = ParseLong(args, "--triples", DefaultTripleCount);
        int repeats = Math.Max(1, (int)ParseLong(args, "--repeats", DefaultRepeats));

        string workingDirectory = Directory.CreateTempSubdirectory("veritas-bulk-load-").FullName;
        try
        {
            string filePath = Path.Combine(workingDirectory, "corpus.nq");
            GeneratedCorpus corpus = GenerateNQuadsFile(filePath, tripleCount);
            long fileBytes = new FileInfo(filePath).Length;

            Console.WriteLine($"[bulk-load] triples={corpus.Total:N0} (default={corpus.DefaultCount:N0}, named={corpus.NamedCount:N0} across {NamedGraphCount} graphs) predicates={PredicateCount} repeats={repeats}");
            Console.WriteLine($"[bulk-load]   corpus file {fileBytes:N0} bytes ({fileBytes / (1024.0 * 1024.0):F1} MB) at {filePath}");

            //One isolated child per route: only a fresh process gives a self-attributable process-lifetime peak
            //(see the type remarks). A failed child has already printed its diagnostics; the run stops non-zero.
            RouteMeasurement? servedStreaming = await RunRouteChildAsync(RouteServedStreaming, filePath, tripleCount, repeats).ConfigureAwait(false);
            RouteMeasurement? servedList = servedStreaming is null ? null : await RunRouteChildAsync(RouteServedList, filePath, tripleCount, repeats).ConfigureAwait(false);
            RouteMeasurement? persistedStreaming = servedList is null ? null : await RunRouteChildAsync(RoutePersistedStreaming, filePath, tripleCount, repeats).ConfigureAwait(false);
            RouteMeasurement? persistedList = persistedStreaming is null ? null : await RunRouteChildAsync(RoutePersistedList, filePath, tripleCount, repeats).ConfigureAwait(false);
            if(servedStreaming is null || servedList is null || persistedStreaming is null || persistedList is null)
            {
                Environment.ExitCode = 1;

                return;
            }

            PrintHalf("Half A (file -> served immutable engine)", servedStreaming, servedList);
            PrintHalf("Half B (file -> persisted mutable generation)", persistedStreaming, persistedList);

            Console.WriteLine(RouteIsolationNote);
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    /// <summary>
    /// Runs one route inside this child process — the <c>--profile-bulk-load-route</c> entry the parent spawns. It
    /// JIT-warms the route over a tiny generated corpus (one untimed same-route warm open plus the probe queries),
    /// measures the route over the parent-supplied corpus, and prints the one machine-readable result line the
    /// parent parses.
    /// </summary>
    /// <param name="args">The command-line arguments: the route name at index 1, then <c>--corpus PATH</c>, <c>--triples N</c>, and <c>--repeats R</c> (clamped to at least 1).</param>
    /// <returns>A task that completes when the route has been measured and its result line printed.</returns>
    /// <exception cref="ArgumentException">The route name is missing or unknown, or the corpus path is missing.</exception>
    public static async Task RunBulkLoadRouteAsync(string[] args)
    {
        string route = args.Length > 1
            ? args[1]
            : throw new ArgumentException("The route name must follow --profile-bulk-load-route.", nameof(args));
        string corpusPath = ParseText(args, "--corpus")
            ?? throw new ArgumentException("A --corpus PATH argument is required.", nameof(args));
        long tripleCount = ParseLong(args, "--triples", DefaultTripleCount);
        int repeats = Math.Max(1, (int)ParseLong(args, "--repeats", DefaultRepeats));
        CancellationToken cancellationToken = CancellationToken.None;

        Func<string, CancellationToken, Task<VeritasEngine>> opener = ResolveOpener(route);
        bool persists = IsPersistedRoute(route);
        GeneratedCorpus corpus = DescribeCorpus(tripleCount);

        string workingDirectory = Directory.CreateTempSubdirectory("veritas-bulk-route-").FullName;
        try
        {
            //The small same-route warm-up: JIT the route's own code and the probe queries before the timed reps. It
            //contributes to this process's peak high-water, which is why it stays tiny (see the type remarks).
            string warmupFile = Path.Combine(workingDirectory, "warmup.nq");
            GeneratedCorpus warmupCorpus = GenerateNQuadsFile(warmupFile, WarmupTripleCount);
            VeritasEngine warmEngine = await opener(warmupFile, cancellationToken).ConfigureAwait(false);
            if(persists)
            {
                warmEngine.Persist(new FileSystemPersistenceStore(CreateStore(workingDirectory, "warm-store")));
            }

            _ = await ProveServesAsync(warmEngine, warmupCorpus, cancellationToken).ConfigureAwait(false);
            _ = await CountDefaultGraphAsync(warmEngine, cancellationToken).ConfigureAwait(false);
            await warmEngine.DisposeAsync().ConfigureAwait(false);

            RouteMeasurement measurement = persists
                ? await MeasurePersistedAsync(Path.Combine(workingDirectory, "stores"), () => opener(corpusPath, cancellationToken), corpus, repeats, cancellationToken).ConfigureAwait(false)
                : await MeasureServedAsync(() => opener(corpusPath, cancellationToken), corpus, repeats, cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"{RouteLinePrefix}route={route} medianMs={measurement.MedianMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} allocatedBytes={measurement.AllocatedBytes.ToString(CultureInfo.InvariantCulture)} peakWorkingSetBytes={measurement.PeakWorkingSetBytes.ToString(CultureInfo.InvariantCulture)} storeBytes={measurement.StoreBytes.ToString(CultureInfo.InvariantCulture)} served={(measurement.Served ? "true" : "false")} defaultCount={measurement.ServedDefaultCount}");
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    /// <summary>Resolves a route name to its opener over a corpus path.</summary>
    /// <param name="route">The route name.</param>
    /// <returns>The opener.</returns>
    /// <exception cref="ArgumentException">The route name is unknown.</exception>
    private static Func<string, CancellationToken, Task<VeritasEngine>> ResolveOpener(string route)
    {
        return route switch
        {
            RouteServedStreaming => OpenServedStreamingAsync,
            RouteServedList => OpenServedListAsync,
            RoutePersistedStreaming => OpenPersistedStreamingAsync,
            RoutePersistedList => OpenPersistedListAsync,
            _ => throw new ArgumentException($"Unknown bulk-load route '{route}'.", nameof(route)),
        };
    }

    /// <summary>Whether a route persists a generation (Half B) rather than only serving (Half A).</summary>
    /// <param name="route">The route name.</param>
    /// <returns><see langword="true"/> for a persisted route.</returns>
    private static bool IsPersistedRoute(string route)
    {
        return route is RoutePersistedStreaming or RoutePersistedList;
    }

    /// <summary>
    /// Spawns one route child process over the shared corpus, waits for it, and parses its result line. A failing
    /// child (non-zero exit, no result line, or a malformed line) has its full output printed and yields
    /// <see langword="null"/> so the parent can stop non-zero.
    /// </summary>
    /// <param name="route">The route name the child measures.</param>
    /// <param name="corpusPath">The parent-generated corpus path.</param>
    /// <param name="tripleCount">The corpus's total triple count (the child re-derives the probe from it).</param>
    /// <param name="repeats">The number of timed repetitions.</param>
    /// <returns>The parsed measurement, or <see langword="null"/> when the child failed.</returns>
    private static async Task<RouteMeasurement?> RunRouteChildAsync(string route, string corpusPath, long tripleCount, int repeats)
    {
        string? processPath = Environment.ProcessPath;
        if(processPath is null)
        {
            Console.WriteLine($"[bulk-load]   route {route}: FAILED — the host process path is unavailable, so no child can be spawned.");

            return null;
        }

        Console.WriteLine($"[bulk-load]   route {route}: measuring in an isolated child process");

        ProcessStartInfo start = new(processPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--profile-bulk-load-route");
        start.ArgumentList.Add(route);
        start.ArgumentList.Add("--corpus");
        start.ArgumentList.Add(corpusPath);
        start.ArgumentList.Add("--triples");
        start.ArgumentList.Add(tripleCount.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--repeats");
        start.ArgumentList.Add(repeats.ToString(CultureInfo.InvariantCulture));

        using Process? child = Process.Start(start);
        if(child is null)
        {
            Console.WriteLine($"[bulk-load]   route {route}: FAILED — the child process did not start.");

            return null;
        }

        //Both streams are drained concurrently with the wait so a full pipe can never deadlock the child.
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().ConfigureAwait(false);
        string output = await stdout.ConfigureAwait(false);
        string errors = await stderr.ConfigureAwait(false);

        if(child.ExitCode != 0)
        {
            Console.WriteLine($"[bulk-load]   route {route}: FAILED — child exit code {child.ExitCode}. Child output follows.");
            Console.WriteLine(output);
            Console.WriteLine(errors);

            return null;
        }

        RouteMeasurement? measurement = ParseRouteResult(output, route);
        if(measurement is null)
        {
            Console.WriteLine($"[bulk-load]   route {route}: FAILED — no parseable result line in the child output. Child output follows.");
            Console.WriteLine(output);
            Console.WriteLine(errors);
        }

        return measurement;
    }

    /// <summary>Parses a route child's machine-readable result line into a measurement.</summary>
    /// <param name="output">The child's full standard output.</param>
    /// <param name="route">The route the line must name.</param>
    /// <returns>The measurement, or <see langword="null"/> when no line matches or a field does not parse.</returns>
    private static RouteMeasurement? ParseRouteResult(string output, string route)
    {
        foreach(string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if(!line.StartsWith(RouteLinePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            Dictionary<string, string> fields = [];
            foreach(string token in line[RouteLinePrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = token.IndexOf('=', StringComparison.Ordinal);
                if(separator > 0)
                {
                    fields[token[..separator]] = token[(separator + 1)..];
                }
            }

            if(!fields.TryGetValue("route", out string? namedRoute) || namedRoute != route)
            {
                continue;
            }

            if(fields.TryGetValue("medianMs", out string? medianText)
                && double.TryParse(medianText, NumberStyles.Float, CultureInfo.InvariantCulture, out double medianMs)
                && fields.TryGetValue("allocatedBytes", out string? allocatedText)
                && long.TryParse(allocatedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long allocatedBytes)
                && fields.TryGetValue("peakWorkingSetBytes", out string? peakText)
                && long.TryParse(peakText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long peakBytes)
                && fields.TryGetValue("storeBytes", out string? storeText)
                && long.TryParse(storeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long storeBytes)
                && fields.TryGetValue("served", out string? servedText)
                && fields.TryGetValue("defaultCount", out string? defaultCount))
            {
                return new RouteMeasurement(medianMs, allocatedBytes, peakBytes, storeBytes, servedText == "true", defaultCount);
            }
        }

        return null;
    }

    /// <summary>Prints one half's streaming and list measurements plus the streaming/list allocation and peak ratios.</summary>
    /// <param name="title">The half's title line.</param>
    /// <param name="streaming">The streaming-route measurement.</param>
    /// <param name="list">The list-route measurement.</param>
    private static void PrintHalf(string title, RouteMeasurement streaming, RouteMeasurement list)
    {
        Console.WriteLine($"[bulk-load] {title}");
        Console.WriteLine($"[bulk-load]   {"streaming",-10} median = {streaming.MedianMilliseconds,9:F1} ms  allocated = {streaming.AllocatedBytes / (1024.0 * 1024.0),8:F1} MB  peak WS = {streaming.PeakWorkingSetBytes / (1024.0 * 1024.0),8:F1} MB{FormatStore(streaming)}  serves={(streaming.Served ? "yes" : "NO")} default-count={streaming.ServedDefaultCount}");
        Console.WriteLine($"[bulk-load]   {"list",-10} median = {list.MedianMilliseconds,9:F1} ms  allocated = {list.AllocatedBytes / (1024.0 * 1024.0),8:F1} MB  peak WS = {list.PeakWorkingSetBytes / (1024.0 * 1024.0),8:F1} MB{FormatStore(list)}  serves={(list.Served ? "yes" : "NO")} default-count={list.ServedDefaultCount}");
        double allocRatio = list.AllocatedBytes == 0 ? 0.0 : (double)streaming.AllocatedBytes / list.AllocatedBytes;
        double peakRatio = list.PeakWorkingSetBytes == 0 ? 0.0 : (double)streaming.PeakWorkingSetBytes / list.PeakWorkingSetBytes;
        Console.WriteLine($"[bulk-load]   ratio streaming/list  alloc = {allocRatio:F2}  peak = {peakRatio:F2}");
    }

    /// <summary>Renders the persisted store size for a Half B measurement, or nothing for a Half A (served) measurement.</summary>
    /// <param name="measurement">The route measurement.</param>
    /// <returns>The store-size fragment, or an empty string.</returns>
    private static string FormatStore(RouteMeasurement measurement)
    {
        return measurement.StoreBytes < 0
            ? string.Empty
            : $"  store = {measurement.StoreBytes / (1024.0 * 1024.0),8:F1} MB";
    }

    /// <summary>Measures a served-engine (Half A) route over <paramref name="repeats"/> repetitions: the timed window is the open alone; wall-clock and allocation both report the median over the repetitions, and serving is proven and counted out of band on the last repetition.</summary>
    /// <param name="openAsync">The factory that performs one open and returns the served engine.</param>
    /// <param name="corpus">The generated corpus, for the serve probe and the expected default count.</param>
    /// <param name="repeats">The number of timed repetitions.</param>
    /// <param name="cancellationToken">A token that aborts the measurement.</param>
    /// <returns>The route measurement.</returns>
    private static async Task<RouteMeasurement> MeasureServedAsync(Func<Task<VeritasEngine>> openAsync, GeneratedCorpus corpus, int repeats, CancellationToken cancellationToken)
    {
        Settle();

        double[] millis = new double[repeats];
        double[] allocs = new double[repeats];
        bool served = false;
        string servedCount = "n/a";
        for(int repetition = 0; repetition < repeats; repetition++)
        {
            long beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
            long startTimestamp = Stopwatch.GetTimestamp();
            VeritasEngine engine = await openAsync().ConfigureAwait(false);
            millis[repetition] = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            allocs[repetition] = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated;

            await using(engine.ConfigureAwait(false))
            {
                if(repetition == repeats - 1)
                {
                    served = await ProveServesAsync(engine, corpus, cancellationToken).ConfigureAwait(false);
                    servedCount = await CountDefaultGraphAsync(engine, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        long peak = Process.GetCurrentProcess().PeakWorkingSet64;

        return new RouteMeasurement(Median(millis), (long)Median(allocs), peak, StoreBytes: -1, served, servedCount);
    }

    /// <summary>Measures a persisted-generation (Half B) route over <paramref name="repeats"/> repetitions: the timed window is the open plus <see cref="VeritasEngine.Persist"/>; wall-clock and allocation both report the median over the repetitions, and the persisted store size and the serve probe are taken out of band on the last repetition.</summary>
    /// <param name="storeRoot">The directory under which each repetition's fresh store is created.</param>
    /// <param name="openAsync">The factory that performs one mutable open and returns the engine.</param>
    /// <param name="corpus">The generated corpus, for the serve probe and the expected default count.</param>
    /// <param name="repeats">The number of timed repetitions.</param>
    /// <param name="cancellationToken">A token that aborts the measurement.</param>
    /// <returns>The route measurement, carrying the persisted store size.</returns>
    private static async Task<RouteMeasurement> MeasurePersistedAsync(string storeRoot, Func<Task<VeritasEngine>> openAsync, GeneratedCorpus corpus, int repeats, CancellationToken cancellationToken)
    {
        Settle();

        double[] millis = new double[repeats];
        double[] allocs = new double[repeats];
        long storeBytes = 0;
        bool served = false;
        string servedCount = "n/a";
        for(int repetition = 0; repetition < repeats; repetition++)
        {
            string repetitionDirectory = Path.Combine(storeRoot, "rep" + repetition.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(repetitionDirectory);
            FileSystemPersistenceStore store = new(repetitionDirectory);

            long beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
            long startTimestamp = Stopwatch.GetTimestamp();
            VeritasEngine engine = await openAsync().ConfigureAwait(false);
            engine.Persist(store);
            millis[repetition] = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            allocs[repetition] = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated;

            await using(engine.ConfigureAwait(false))
            {
                if(repetition == repeats - 1)
                {
                    served = await ProveServesAsync(engine, corpus, cancellationToken).ConfigureAwait(false);
                    servedCount = await CountDefaultGraphAsync(engine, cancellationToken).ConfigureAwait(false);
                    storeBytes = DirectoryByteSize(repetitionDirectory);
                }
            }
        }

        long peak = Process.GetCurrentProcess().PeakWorkingSet64;

        return new RouteMeasurement(Median(millis), (long)Median(allocs), peak, storeBytes, served, servedCount);
    }

    /// <summary>Opens a served immutable engine by streaming the corpus file through the real N-Quads-parser-over-a-pipe route.</summary>
    /// <param name="filePath">The N-Quads corpus path.</param>
    /// <param name="cancellationToken">A token that aborts the open.</param>
    /// <returns>The served engine.</returns>
    private static async Task<VeritasEngine> OpenServedStreamingAsync(string filePath, CancellationToken cancellationToken)
    {
        return await VeritasEngine.OpenAsync(StreamFileAsync(filePath, cancellationToken), IngestOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a served immutable engine by materialising the corpus into default and named triple lists first, then opening through the genuine list overload.</summary>
    /// <param name="filePath">The N-Quads corpus path.</param>
    /// <param name="cancellationToken">A token that aborts the open.</param>
    /// <returns>The served engine.</returns>
    private static async Task<VeritasEngine> OpenServedListAsync(string filePath, CancellationToken cancellationToken)
    {
        (List<DataTriple> defaultGraph, List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs) = await MaterialiseTripleListsAsync(filePath, cancellationToken).ConfigureAwait(false);

        return await VeritasEngine.OpenAsync(defaultGraph, namedGraphs, IngestOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a mutable engine by streaming the corpus file through the real N-Quads-parser-over-a-pipe route.</summary>
    /// <param name="filePath">The N-Quads corpus path.</param>
    /// <param name="cancellationToken">A token that aborts the open.</param>
    /// <returns>The mutable engine.</returns>
    private static async Task<VeritasEngine> OpenPersistedStreamingAsync(string filePath, CancellationToken cancellationToken)
    {
        return await VeritasEngine.OpenMutableAsync(StreamFileAsync(filePath, cancellationToken), IngestOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a mutable engine by materialising the whole corpus into a quad list first, then opening that retained list through the quad-stream mutable open (the mutable API has no named-graph list overload; the retained list is the composition cost).</summary>
    /// <param name="filePath">The N-Quads corpus path.</param>
    /// <param name="cancellationToken">A token that aborts the open.</param>
    /// <returns>The mutable engine.</returns>
    private static async Task<VeritasEngine> OpenPersistedListAsync(string filePath, CancellationToken cancellationToken)
    {
        List<Quad> quads = await MaterialiseQuadListAsync(filePath, cancellationToken).ConfigureAwait(false);

        return await VeritasEngine.OpenMutableAsync(ToAsync(quads, cancellationToken), IngestOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Streams a corpus file's quads through the FileStream-to-PipeReader-to-N-Quads-reader pipeline the CLI loader wires, encoding each quad as it arrives.</summary>
    /// <param name="filePath">The N-Quads corpus path.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The corpus's quads.</returns>
    private static async IAsyncEnumerable<Quad> StreamFileAsync(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //A sequential-scan read stream feeding a PipeReader is the same shape the CLI streaming loader uses; the
        //benchmark harness is exempt from the production FileStream ban, so a plain FileStream stands in for the
        //CLI's SafeFileHandle wrapper — the measurement targets the engine path, not the file API.
        using FileStream stream = new(filePath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        await foreach(Quad quad in NQuadsReader.ReadAsync(reader, pool: null, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return quad;
        }
    }

    /// <summary>Parses the whole corpus into a materialised default-graph triple list and per-named-graph triple lists — the list-route composition the streaming route avoids.</summary>
    /// <param name="filePath">The N-Quads corpus path.</param>
    /// <param name="cancellationToken">A token that aborts the parse.</param>
    /// <returns>The default-graph triples and the named graphs, each its graph-name term paired with its triples.</returns>
    private static async Task<(List<DataTriple> DefaultGraph, List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> NamedGraphs)> MaterialiseTripleListsAsync(string filePath, CancellationToken cancellationToken)
    {
        List<DataTriple> defaultGraph = [];
        Dictionary<string, List<DataTriple>> buckets = [];
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs = [];
        await foreach(Quad quad in StreamFileAsync(filePath, cancellationToken).ConfigureAwait(false))
        {
            DataTriple triple = new(quad.Subject, quad.Predicate, quad.Object);
            if(quad.Graph is null)
            {
                defaultGraph.Add(triple);
            }
            else
            {
                string key = quad.Graph.ToString();
                if(!buckets.TryGetValue(key, out List<DataTriple>? bucket))
                {
                    bucket = [];
                    buckets[key] = bucket;
                    namedGraphs.Add((quad.Graph, bucket));
                }

                bucket.Add(triple);
            }
        }

        return (defaultGraph, namedGraphs);
    }

    /// <summary>Parses the whole corpus into one retained quad list — the list-route composition the streaming route avoids.</summary>
    /// <param name="filePath">The N-Quads corpus path.</param>
    /// <param name="cancellationToken">A token that aborts the parse.</param>
    /// <returns>The corpus's quads.</returns>
    private static async Task<List<Quad>> MaterialiseQuadListAsync(string filePath, CancellationToken cancellationToken)
    {
        List<Quad> quads = [];
        await foreach(Quad quad in StreamFileAsync(filePath, cancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return quads;
    }

    /// <summary>Yields a materialised quad list as an async stream, so the retained list feeds the engine through the same quad-stream open a real streaming parser would.</summary>
    /// <param name="quads">The quads to yield.</param>
    /// <param name="cancellationToken">A token that aborts enumeration.</param>
    /// <returns>The quads, yielded asynchronously.</returns>
    private static async IAsyncEnumerable<Quad> ToAsync(List<Quad> quads, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach(Quad quad in quads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return quad;
        }
    }

    /// <summary>Proves the engine serves by asking for a known default-graph triple.</summary>
    /// <param name="engine">The opened engine.</param>
    /// <param name="corpus">The generated corpus, carrying the sample default triple.</param>
    /// <param name="cancellationToken">A token that aborts the query.</param>
    /// <returns><see langword="true"/> when the sample triple is served.</returns>
    private static async Task<bool> ProveServesAsync(VeritasEngine engine, GeneratedCorpus corpus, CancellationToken cancellationToken)
    {
        string ask = $"ASK {{ <{corpus.SampleSubject}> <{corpus.SamplePredicate}> <{corpus.SampleObject}> }}";

        return await engine.AskAsync(Utf8Strings.From(ask), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Counts the served default graph via a <c>COUNT(*)</c> aggregate and returns the count's rendered value — a cheap cross-check that the engine serves the ingested triples.</summary>
    /// <param name="engine">The opened engine.</param>
    /// <param name="cancellationToken">A token that aborts the query.</param>
    /// <returns>The rendered count value, or a placeholder when no binding came back.</returns>
    private static async Task<string> CountDefaultGraphAsync(VeritasEngine engine, CancellationToken cancellationToken)
    {
        VeritasQueryResult result = await engine.QueryAsync(Utf8Strings.From("SELECT (COUNT(*) AS ?count) WHERE { ?s ?p ?o }"), cancellationToken: cancellationToken).ConfigureAwait(false);
        if(result.Bindings is { Solutions.Count: > 0 } bindings && bindings.Solutions[0].Bindings.Count > 0)
        {
            return bindings.Solutions[0].Bindings[0].Value.ToString();
        }

        return "?";
    }

    /// <summary>Generates a deterministic N-Quads corpus streamed to a file: a chain over the default graph with a shared predicate vocabulary, and every eighth triple routed round-robin into one of a few named graphs.</summary>
    /// <param name="filePath">The path the corpus is written to.</param>
    /// <param name="tripleCount">The total number of triples to generate across the default and named graphs.</param>
    /// <returns>The corpus description, identical to <see cref="DescribeCorpus"/> over the same count.</returns>
    private static GeneratedCorpus GenerateNQuadsFile(string filePath, long tripleCount)
    {
        using StreamWriter writer = new(filePath);
        for(long index = 0; index < tripleCount; index++)
        {
            string subjectId = index.ToString(CultureInfo.InvariantCulture);
            string objectId = (index + 1).ToString(CultureInfo.InvariantCulture);
            string predicateId = (index % PredicateCount).ToString(CultureInfo.InvariantCulture);
            string subject = $"{Base}n{subjectId}";
            string predicate = $"{Base}p{predicateId}";
            string @object = $"{Base}n{objectId}";

            if(index % NamedGraphEvery == 0)
            {
                string graphId = ((index / NamedGraphEvery) % NamedGraphCount).ToString(CultureInfo.InvariantCulture);
                writer.Write($"<{subject}> <{predicate}> <{@object}> <{Base}g{graphId}> .\n");
            }
            else
            {
                writer.Write($"<{subject}> <{predicate}> <{@object}> .\n");
            }
        }

        return DescribeCorpus(tripleCount);
    }

    /// <summary>Describes the deterministic corpus a given triple count generates, without reading it: the default and named counts follow from the every-eighth routing, and the sample default triple is the chain's first default edge (index 1). The parent and each route child derive the same description independently.</summary>
    /// <param name="tripleCount">The total triple count.</param>
    /// <returns>The corpus description.</returns>
    private static GeneratedCorpus DescribeCorpus(long tripleCount)
    {
        long namedCount = (tripleCount + NamedGraphEvery - 1) / NamedGraphEvery;
        long defaultCount = tripleCount - namedCount;

        //A degenerate one-triple corpus routes its only triple (index 0) into a named graph; the fallback probe then
        //truthfully reports not-served for the default graph.
        return tripleCount > 1
            ? new GeneratedCorpus(defaultCount, namedCount, $"{Base}n1", $"{Base}p1", $"{Base}n2")
            : new GeneratedCorpus(defaultCount, namedCount, $"{Base}n0", $"{Base}p0", $"{Base}n1");
    }

    /// <summary>Sums the byte length of every file directly under a directory — the persisted store's on-disk size.</summary>
    /// <param name="directory">The store directory.</param>
    /// <returns>The total byte size.</returns>
    private static long DirectoryByteSize(string directory)
    {
        long total = 0;
        foreach(string path in Directory.EnumerateFiles(directory))
        {
            total += new FileInfo(path).Length;
        }

        return total;
    }

    /// <summary>Reclaims the warm-up's garbage before the route is measured; a full blocking collection keeps live memory low so the measured repetitions start from a settled heap.</summary>
    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>The median of a sample; a single sample is its own median.</summary>
    /// <param name="values">The samples.</param>
    /// <returns>The median.</returns>
    private static double Median(double[] values)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int count = sorted.Length;

        return count % 2 == 1
            ? sorted[count / 2]
            : (sorted[(count / 2) - 1] + sorted[count / 2]) / 2.0;
    }

    /// <summary>Parses a <c>--name value</c> long argument, defaulting when absent or unparsable.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="name">The argument name.</param>
    /// <param name="fallback">The value to use when the argument is absent or unparsable.</param>
    /// <returns>The parsed value, or <paramref name="fallback"/>.</returns>
    private static long ParseLong(string[] args, string name, long fallback)
    {
        for(int index = 0; index < args.Length - 1; index++)
        {
            if(args[index] == name && long.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    /// <summary>Parses a <c>--name value</c> text argument.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/> when absent.</returns>
    private static string? ParseText(string[] args, string name)
    {
        for(int index = 0; index < args.Length - 1; index++)
        {
            if(args[index] == name)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>Creates and returns a fresh store directory under the working directory.</summary>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="name">The store subdirectory name.</param>
    /// <returns>The created store directory path.</returns>
    private static string CreateStore(string workingDirectory, string name)
    {
        string directory = Path.Combine(workingDirectory, name);
        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>Deletes the working directory best-effort; a leftover temp directory is not worth failing the run over.</summary>
    /// <param name="directory">The directory to delete.</param>
    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch(IOException)
        {
        }
        catch(UnauthorizedAccessException)
        {
        }
    }

    /// <summary>One route's measurement: median wall-clock and allocation over the repetitions, peak working set, persisted store size (or -1 for a served route), and the serve probe result.</summary>
    /// <param name="MedianMilliseconds">The median wall-clock over the repetitions.</param>
    /// <param name="AllocatedBytes">The median <see cref="GC.GetTotalAllocatedBytes(bool)"/> delta over the repetitions.</param>
    /// <param name="PeakWorkingSetBytes">The route child's process peak working set after its repetitions.</param>
    /// <param name="StoreBytes">The persisted store's on-disk size, or -1 for a served (non-persisted) route.</param>
    /// <param name="Served">Whether the serve probe answered true.</param>
    /// <param name="ServedDefaultCount">The served default-graph <c>COUNT(*)</c> value.</param>
    private sealed record RouteMeasurement(double MedianMilliseconds, long AllocatedBytes, long PeakWorkingSetBytes, long StoreBytes, bool Served, string ServedDefaultCount);

    /// <summary>The deterministic corpus description: the default and named triple counts and a sample default triple for the serve probe.</summary>
    /// <param name="DefaultCount">The number of default-graph triples.</param>
    /// <param name="NamedCount">The number of named-graph triples.</param>
    /// <param name="SampleSubject">The sample default triple's subject IRI.</param>
    /// <param name="SamplePredicate">The sample default triple's predicate IRI.</param>
    /// <param name="SampleObject">The sample default triple's object IRI.</param>
    private readonly record struct GeneratedCorpus(long DefaultCount, long NamedCount, string SampleSubject, string SamplePredicate, string SampleObject)
    {
        /// <summary>The total triple count across the default and named graphs.</summary>
        public long Total => DefaultCount + NamedCount;
    }
}
