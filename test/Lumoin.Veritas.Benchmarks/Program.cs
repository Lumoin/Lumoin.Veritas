using BenchmarkDotNet.Running;

namespace Lumoin.Veritas.Benchmarks;

internal static class Program
{
    /// <summary>
    /// Runs benchmarks via BenchmarkDotNet's
    /// <see cref="BenchmarkSwitcher"/>, which discovers every
    /// public type with at least one <c>[Benchmark]</c> method in
    /// the executing assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Common invocations: <c>dotnet run -c Release</c> launches
    /// an interactive selector; <c>dotnet run -c Release -- --filter "*"</c>
    /// runs everything; <c>dotnet run -c Release -- --filter "*BuildBenchmark*"</c>
    /// runs one benchmark class.
    /// </para>
    /// <para>
    /// BenchmarkDotNet's own argument parser handles every flag
    /// after the bare <c>--</c>, so the entry point itself does
    /// not need to interpret <paramref name="args"/>.
    /// </para>
    /// </remarks>
    public static void Main(string[] args)
    {
        //Profile-mode entrypoints for dotnet-trace / PerfView: these
        //run a long-lived loop in the host process so a sampling
        //profiler sees real call-stack samples without BenchmarkDotNet's
        //fork-per-iteration orchestration in the way.
        if(args.Length > 0 && args[0] == "--profile-cid")
        {
            CarParseSoak.RunCidSoak(System.TimeSpan.FromSeconds(20));
            return;
        }

        if(args.Length > 0 && args[0] == "--profile-car")
        {
            CarParseSoak.RunCarBlockSoak(System.TimeSpan.FromSeconds(20));
            return;
        }

        if(args.Length > 0 && args[0] == "--profile-car-interned")
        {
            CarParseSoak.RunCarBlockSoakInterned(System.TimeSpan.FromSeconds(20));
            return;
        }

        if(args.Length > 0 && args[0] == "--profile-car-reuse")
        {
            CarParseSoak.RunCarBlockSoakReuse(System.TimeSpan.FromSeconds(20));
            return;
        }

        if(args.Length > 0 && args[0] == "--profile-car-all")
        {
            //Combined comparison run: baseline, +intern, +intern+reuse,
            //all over the same fixture. Print throughput at the end so
            //a single trace shows the delta.
            CarParseSoak.RunCarBlockSoak(System.TimeSpan.FromSeconds(15));
            CarParseSoak.RunCarBlockSoakInterned(System.TimeSpan.FromSeconds(15));
            CarParseSoak.RunCarBlockSoakReuse(System.TimeSpan.FromSeconds(15));
            return;
        }

        //WCOJ query soak: loops the QueryBenchmark shapes in the
        //host process so a sampling profiler sees the join driver's
        //real stacks.
        if(args.Length > 0 && args[0] == "--profile-wcoj-query")
        {
            WcojQuerySoak.RunQuerySoakAsync(10_000, System.TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-wcoj-query-100k")
        {
            WcojQuerySoak.RunQuerySoakAsync(100_000, System.TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-wcoj-query-1m")
        {
            WcojQuerySoak.RunQuerySoakAsync(1_000_000, System.TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

            return;
        }

        //Property-path soak entrypoints. Each cell builds a fresh
        //store, runs three repetitions per path shape, and prints
        //min/median/max wall-clock plus allocation, GC, and peak
        //working-set metrics. Run cells separately so a 10M failure
        //in one cell does not block the others.
        if(args.Length > 0 && args[0] == "--profile-path-chain-100k")
        {
            PropertyPathSoak.RunChainSoakAsync(100_000).GetAwaiter().GetResult();

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-chain-1m")
        {
            PropertyPathSoak.RunChainSoakAsync(1_000_000).GetAwaiter().GetResult();

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-chain-10m")
        {
            PropertyPathSoak.RunChainSoakAsync(10_000_000).GetAwaiter().GetResult();

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-smallworld-100k")
        {
            PropertyPathSoak.RunSmallWorldSoakAsync(100_000).GetAwaiter().GetResult();

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-smallworld-1m")
        {
            PropertyPathSoak.RunSmallWorldSoakAsync(1_000_000).GetAwaiter().GetResult();

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-smallworld-10m")
        {
            PropertyPathSoak.RunSmallWorldSoakAsync(10_000_000).GetAwaiter().GetResult();

            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
