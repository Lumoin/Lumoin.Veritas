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

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
