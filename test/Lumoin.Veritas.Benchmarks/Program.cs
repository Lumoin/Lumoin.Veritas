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
        //Numeric output is for hand-collation across machines, so it formats
        //culture-invariantly (a dot decimal) rather than picking up the host's
        //locale.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

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

        //The owl:sameAs ladder: eq-* rule materialization vs union-find
        //canonicalization, with a differential check at the small rung.
        if(args.Length > 0 && args[0] == "--profile-owl-sameas")
        {
            OwlSameAsSoak.RunSameAsSoak();

            return;
        }

        //The SPARQL solution-layer join: nested-loop compatibility merge
        //vs the shared-variable hash join, over the same materialised
        //solution sequences, across a selectivity ladder.
        if(args.Length > 0 && args[0] == "--profile-solution-join")
        {
            SolutionJoinSoak.RunSolutionJoinSoak();

            return;
        }

        //The 3B diagnostic: how much of a query's cost is the
        //row-materialization (BGP flatten/decode + Merge) that a
        //column-major batch substrate would remove - a full two-hop
        //join vs the same join filtered to a tiny output.
        if(args.Length > 0 && args[0] == "--profile-sparql-pipeline")
        {
            SparqlPipelineSoak.RunSparqlPipelineSoak();

            return;
        }

        //Yannakakis semijoin reduction vs the unreduced left-deep
        //stream on a chain whose middle join blows up but whose output
        //is small - the intermediate shrink the reduction guarantees.
        if(args.Length > 0 && args[0] == "--profile-yannakakis")
        {
            YannakakisSoak.RunYannakakisSoak();

            return;
        }

        //Factorised intermediates vs the flat row product on a fan-out join
        //whose result is a per-key cross product - the stored-size shrink the
        //product-of-unions representation guarantees.
        if(args.Length > 0 && args[0] == "--profile-factorized")
        {
            FactorizationSoak.RunFactorizationSoak();

            return;
        }

        //The open-addressed (Swiss-table) join-table head map vs the chained
        //Dictionary-backed one: build CPU, allocation, probe CPU, and memory.
        if(args.Length > 0 && args[0] == "--profile-jointable")
        {
            JoinTableSoak.RunJoinTableSoak();

            return;
        }

        //The columnar triple index's packed column footprint as bits per triple,
        //per order and total, across order-set modes.
        if(args.Length > 0 && args[0] == "--profile-column-bits")
        {
            ColumnFootprintSoak.RunColumnFootprintSoak();

            return;
        }

        //Elias-Fano succinct column encoding vs frame-of-reference: bits/value
        //and seek cost across universe/count ratios (the ~10-vs-~22 comparison).
        if(args.Length > 0 && args[0] == "--profile-elias-fano")
        {
            EliasFanoSoak.RunEliasFanoSoak();

            return;
        }

        //Elias-Fano vs the index's current per-column encoding on the REAL
        //columns of a built columnar index: per-column bits/value and
        //bits/triple, monotone columns rebuilt as Elias-Fano in place.
        if(args.Length > 0 && args[0] == "--profile-elias-fano-columns")
        {
            EliasFanoSoak.RunColumnComparisonSoak();

            return;
        }

        //Storage at scale: uncompressed baseline vs the block-packed index vs
        //the index with Elias-Fano on the monotone value columns, bits/triple
        //over a triple-count ladder.
        if(args.Length > 0 && args[0] == "--profile-storage-comparison")
        {
            StorageComparisonSoak.RunStorageComparisonSoak();

            return;
        }

        //Partitioned Elias-Fano on the within-group level-2 value column vs
        //frame of reference, swept over per-group fan-out to pin the break-even.
        if(args.Length > 0 && args[0] == "--profile-partitioned-elias-fano")
        {
            EliasFanoSoak.RunPartitionedComparisonSoak();

            return;
        }

        //Identifier-assignment effect: a community-structured graph encoded with
        //scattered vs clustered ids, frame of reference and Elias-Fano, to see
        //whether exploiting data self-similarity shrinks the footprint.
        if(args.Length > 0 && args[0] == "--profile-id-reassignment")
        {
            IdReassignmentSoak.RunIdReassignmentSoak();

            return;
        }

        //Elias-Fano select-density sweep: footprint and seek vs frame of
        //reference across select-sample rates, to find how far the samples can
        //be sparsified while the seek stays ahead.
        if(args.Length > 0 && args[0] == "--profile-elias-fano-select")
        {
            EliasFanoSoak.RunSelectDensitySoak();

            return;
        }

        //Columnar statistics flowed through the trace channel: each order's
        //cardinalities, fan-out distribution, and bits/triple, emitted as trace
        //events and drained to a per-order table.
        if(args.Length > 0 && args[0] == "--profile-statistics")
        {
            StatisticsSoak.RunStatisticsSoak();

            return;
        }

        //The index-arity bounding probe: per-graph store composition vs
        //one merged store, build and query, over a graph-count ladder.
        if(args.Length > 0 && args[0] == "--profile-graph-fanout")
        {
            GraphFanOutSoak.RunGraphFanOutSoakAsync().GetAwaiter().GetResult();

            return;
        }

        //The layout-spike fixtures the bounding probe left open:
        //resident per-store memory and the ?g-joins-with-data shape.
        if(args.Length > 0 && args[0] == "--profile-graph-spike")
        {
            GraphFanOutSoak.RunGraphSpikeSoakAsync().GetAwaiter().GetResult();

            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
