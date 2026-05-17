using System;
using System.Globalization;
using System.Threading.Tasks;

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
/// </code>
/// or, with <c>dotnet-trace</c> attached:
/// <code>
/// dotnet build -c Release test/Lumoin.Veritas.Workbench
/// dotnet-trace collect --format Speedscope --output build-soak.nettrace -- ^
///   dotnet test/Lumoin.Veritas.Workbench/bin/Release/net10.0/Lumoin.Veritas.Workbench.dll --profile-build
/// </code>
/// </para>
/// <para>
/// <b>Future shape.</b> The current argv-walk is suitable for two
/// commands plus optional flags. When the scenario set grows past
/// roughly four commands, or sub-commands appear, swap in
/// <c>System.CommandLine</c> and reconstruct the root command shape
/// in a test helper analogous to the <c>BuildTestableRootCommand</c>
/// pattern. Out of scope for this batch.
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
    public static async Task<int> Main(string[] args)
    {
        if(args.Length == 0 || IsHelpRequest(args[0]))
        {
            PrintUsage(Console.Out);

            return 0;
        }

        if(!TryParseOptions(args, out TimeSpan duration, out int tripleCount, out string? error))
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            PrintUsage(Console.Error);

            return 1;
        }

        switch(args[0])
        {
            case "--profile-build":
            {
                await Console.Out.WriteLineAsync($"[soak] hypertrie build, {tripleCount:N0} triples, target duration {duration.TotalSeconds:F0}s").ConfigureAwait(false);
                SoakResult result = await HypertrieSoak.RunBuildSoakAsync(duration, tripleCount).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(FormatBuildResult(result, tripleCount)).ConfigureAwait(false);

                return 0;
            }
            case "--profile-query":
            {
                await Console.Out.WriteLineAsync($"[soak] hypertrie query, {tripleCount:N0} triples, target duration {duration.TotalSeconds:F0}s").ConfigureAwait(false);
                SoakResult result = await HypertrieSoak.RunQuerySoakAsync(duration, tripleCount).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(FormatQueryResult(result)).ConfigureAwait(false);

                return 0;
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

    private static bool TryParseOptions(
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

    private static string FormatBuildResult(SoakResult result, int tripleCount)
    {
        double secs = result.Elapsed.TotalSeconds;
        double iterPerSec = secs > 0 ? result.Iterations / secs : 0;
        double triplesPerSec = secs > 0 ? (double)tripleCount * result.Iterations / secs : 0;

        return $"[soak] {result.Iterations:N0} build iterations in {secs:F2}s " +
            $"= {iterPerSec:N0} iter/s ({triplesPerSec:N0} triples/s)";
    }

    private static string FormatQueryResult(SoakResult result)
    {
        double secs = result.Elapsed.TotalSeconds;
        double iterPerSec = secs > 0 ? result.Iterations / secs : 0;

        return $"[soak] {result.Iterations:N0} query iterations in {secs:F2}s " +
            $"= {iterPerSec:N0} iter/s ({result.AuxiliaryCount:N0} total solutions emitted)";
    }

    private static void PrintUsage(System.IO.TextWriter writer)
    {
        writer.WriteLine("Lumoin.Veritas.Workbench");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  workbench --profile-build [--duration <seconds>] [--triple-count <N>]");
        writer.WriteLine("  workbench --profile-query [--duration <seconds>] [--triple-count <N>]");
        writer.WriteLine("  workbench --help");
        writer.WriteLine();
        writer.WriteLine("Defaults:");
        writer.WriteLine($"  --duration       {DefaultDurationSeconds}");
        writer.WriteLine($"  --triple-count   {HypertrieSoak.DefaultTripleCount}");
    }
}
