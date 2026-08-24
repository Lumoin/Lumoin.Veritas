using BenchmarkDotNet.Running;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>The benchmarks host: routes the house soak flags and hands everything else to BenchmarkDotNet.</summary>
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
    public static async System.Threading.Tasks.Task Main(string[] args)
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
        if(args.Length > 0 && args[0] == "--profile-streaming-operators")
        {
            await StreamingOperatorSoak.RunStreamingOperatorSoak().ConfigureAwait(false);
            return;
        }

        if(args.Length > 0 && args[0] == "--profile-rewriter")
        {
            await RewriterSoak.RunRewriterSoak().ConfigureAwait(false);
            return;
        }

        //The disjunctive context-saturation profile: covering-width/depth ladders, the DL4
        //pigeonhole ladder, and the twice-run Horn baseline with the allocation-identity verdict.
        if(args.Length > 0 && args[0] == "--profile-context-disjunction")
        {
            ContextDisjunctionSoak.RunContextDisjunctionSoak();
            return;
        }

        //The nominal context-saturation profile: the root-exchange ladder, the
        //generated-nominal mint ladder with its label-depth curve, and the twice-run Horn and
        //nominal-free baselines with the allocation-identity and nominal-silence verdicts.
        if(args.Length > 0 && args[0] == "--profile-context-nominal")
        {
            ContextNominalSoak.RunContextNominalSoak();
            return;
        }

        //The temporal value-index probe route vs the scan baseline: the seam's read anchors
        //(point window, interval overlap, interval as-of, two no-regression decline rows) and
        //the write anchors (commit tax, commit+probe interleave, rebuild after growth).
        if(args.Length > 0 && args[0] == "--profile-valueindex")
        {
            await ValueIndexSoak.RunValueIndexSoak().ConfigureAwait(false);
            return;
        }

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
            await WcojQuerySoak.RunQuerySoakAsync(10_000, System.TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-wcoj-query-100k")
        {
            await WcojQuerySoak.RunQuerySoakAsync(100_000, System.TimeSpan.FromSeconds(20)).ConfigureAwait(false);

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-wcoj-query-1m")
        {
            await WcojQuerySoak.RunQuerySoakAsync(1_000_000, System.TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            return;
        }

        //Property-path soak entrypoints. Each cell builds a fresh
        //store, runs three repetitions per path shape, and prints
        //min/median/max wall-clock plus allocation, GC, and peak
        //working-set metrics. Run cells separately so a 10M failure
        //in one cell does not block the others.
        if(args.Length > 0 && args[0] == "--profile-path-chain-100k")
        {
            await PropertyPathSoak.RunChainSoakAsync(100_000).ConfigureAwait(false);

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-chain-1m")
        {
            await PropertyPathSoak.RunChainSoakAsync(1_000_000).ConfigureAwait(false);

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-chain-10m")
        {
            await PropertyPathSoak.RunChainSoakAsync(10_000_000).ConfigureAwait(false);

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-smallworld-100k")
        {
            await PropertyPathSoak.RunSmallWorldSoakAsync(100_000).ConfigureAwait(false);

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-smallworld-1m")
        {
            await PropertyPathSoak.RunSmallWorldSoakAsync(1_000_000).ConfigureAwait(false);

            return;
        }

        if(args.Length > 0 && args[0] == "--profile-path-smallworld-10m")
        {
            await PropertyPathSoak.RunSmallWorldSoakAsync(10_000_000).ConfigureAwait(false);

            return;
        }

        //The owl:sameAs ladder: eq-* rule materialization vs union-find
        //canonicalization, with a differential check at the small rung.
        if(args.Length > 0 && args[0] == "--profile-owl-sameas")
        {
            OwlSameAsSoak.RunSameAsSoak();

            return;
        }

        //The shared-subpart DAG baseline: a deterministic Q1-mix
        //corpus (shared-subpart partOf DAG, deep supplies chains,
        //sameAs cliques, flat attribute payload) driven by an append-dominant op stream
        //with retract bursts, measuring full re-materialization cost per
        //checkpoint — the baseline the incremental engine's retract latency is
        //gated against.
        if(args.Length > 0 && args[0] == "--profile-subpart-dag")
        {
            await SharedSubpartDagSoak.RunSharedSubpartDagSoak(args).ConfigureAwait(false);

            return;
        }

        //The description-logic deciding engines (snapshot tableau, SAT-backed,
        //EL fast path) across a ladder of synthetic ontologies, with each
        //decision's wall-clock attributed to its reasoning phases — the cost
        //breakdown that gates blocking-index kernel integration.
        if(args.Length > 0 && args[0] == "--profile-owl-classification")
        {
            await OwlClassificationSoak.RunClassificationSoak(args).ConfigureAwait(false);

            return;
        }

        //The propositional SAT engine the SAT-backed reasoner calls per world:
        //propagation throughput and solve time over random 3-SAT, pigeonhole, and
        //a propagation-heavy chain -- the baseline for a watched-literal rework.
        if(args.Length > 0 && args[0] == "--profile-sat")
        {
            SatSolverSoak.RunSatSoak(args);

            return;
        }

        //The incremental SAT session against fresh per-call solving over one fixed
        //formula and a long sequence of related assumption sets -- the amortization
        //that reusing learned clauses, the variable order, and saved phases buys for
        //the reasoner's repeated per-world solves.
        if(args.Length > 0 && args[0] == "--profile-sat-incremental")
        {
            SatIncrementalSoak.RunIncrementalSoak(args);

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
            await SparqlPipelineSoak.RunSparqlPipelineSoakAsync().ConfigureAwait(false);

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

        //The adaptive join-strategy selector: sweeps star and chain fan-out
        //ladders comparing the streamed, factorised, and adaptively-routed
        //times, locating the time crossover the engagement thresholds carry.
        if(args.Length > 0 && args[0] == "--profile-join-selector")
        {
            JoinSelectorSoak.RunJoinSelectorSoak();

            return;
        }

        //The Free Join route against the engines it interpolates between:
        //star/chain rungs with the batched pipeline as the bar, triangle/
        //lollipop rungs with leapfrog as the bar, each gated on identical row
        //counts, plus the batch-level build-versus-drive split at the
        //join-cover and full depths. --quick runs the smoke protocol.
        if(args.Length > 0 && args[0] == "--profile-free-join")
        {
            await FreeJoinSoak.RunFreeJoinSoakAsync(args).ConfigureAwait(false);

            return;
        }

        //The differential all-routes census: every eligible route over every
        //fixture of the legacy, skew, and shape ladders, gated on identical row
        //counts and emitted as machine-readable fixture, cost, and per-relation
        //lines. --quick runs the smoke protocol.
        if(args.Length > 0 && args[0] == "--profile-join-route-census")
        {
            await JoinRouteCensusSoak.RunJoinRouteCensusAsync(args).ConfigureAwait(false);

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

        //The succinct triple self-index vs the columnar index on the same
        //corpora: bits/triple, build time, equal-semantics membership probes,
        //and the bound-range seek.
        if(args.Length > 0 && args[0] == "--profile-self-index")
        {
            await SelfIndexSoak.RunSelfIndexSoakAsync().ConfigureAwait(false);

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

        //Soak-corpus dump round-trip self-test: save a known corpus to a temp
        //file, load it back, and report whether it survived intact.
        if(args.Length > 0 && args[0] == "--profile-soak-corpus")
        {
            SoakCorpus.RunSelfTest();

            return;
        }

        //The index-arity bounding probe: per-graph store composition vs
        //one merged store, build and query, over a graph-count ladder.
        if(args.Length > 0 && args[0] == "--profile-graph-fanout")
        {
            await GraphFanOutSoak.RunGraphFanOutSoakAsync().ConfigureAwait(false);

            return;
        }

        //The layout-spike fixtures the bounding probe left open:
        //resident per-store memory and the ?g-joins-with-data shape.
        if(args.Length > 0 && args[0] == "--profile-graph-spike")
        {
            await GraphFanOutSoak.RunGraphSpikeSoakAsync().ConfigureAwait(false);

            return;
        }

        //Managed vs native-aligned column payload backing at fixed bytes: the GC-resident
        //reduction (payload off the LOH), build/read cost, and working set — across the
        //frame-of-reference (all block-packed) and Elias-Fano (realistic default) encodings.
        if(args.Length > 0 && args[0] == "--profile-column-backing")
        {
            ColumnBackingSoak.RunColumnBackingSoak();

            return;
        }

        //Turtle ingest allocation/throughput soak: parses a representative generated corpus a fixed
        //number of times and reports exact bytes/parse (GC.GetTotalAllocatedBytes) plus time — the
        //anchor measurement for the parser performance pass.
        if(args.Length > 0 && args[0] == "--profile-turtle-read")
        {
            await TurtleReadSoak.RunTurtleReadSoakAsync().ConfigureAwait(false);

            return;
        }

        //The bulk-load ingest boundary: the streaming quad-stream open versus the list-materialising comparand,
        //over the served-engine (Half A) and persisted-generation (Half B) halves, at a tunable scale, reporting
        //wall-clock, allocation, peak working set, persisted store size, and the streaming/list ratios. The parent
        //spawns one child process per route (the command below) so every peak working set is genuinely per-route.
        if(args.Length > 0 && args[0] == "--profile-bulk-load")
        {
            await BulkLoadSoak.RunBulkLoadSoakAsync(args).ConfigureAwait(false);

            return;
        }

        //The packed box index measurement matrix: both packings across the capacity sweep over
        //deterministic uniform/clustered/archipelago/point-box datasets, a join-cadence rung
        //with a brute-force scan baseline, a thread-scaling rung, and the candidate-digest gate
        //that must agree across every configuration before any number counts. --quick runs the
        //smoke protocol.
        if(args.Length > 0 && args[0] == "--profile-box-index-matrix")
        {
            await BoxIndexMatrixSoak.RunBoxIndexMatrixSoak(args).ConfigureAwait(false);

            return;
        }

        //The containment head-to-head: the dominance tree vs the packed index's containing
        //walk at two configurations vs the brute-force scan, under a cross-path
        //candidate-digest gate, ending with the pre-registered default rule, the per-shape
        //winners, and the join-cadence crossovers. --quick runs the smoke protocol.
        if(args.Length > 0 && args[0] == "--profile-box-containment")
        {
            BoxContainmentHeadToHeadSoak.RunBoxContainmentHeadToHead(args);

            return;
        }

        //One isolated bulk-load route, run inside the child process the parent --profile-bulk-load spawns; it
        //measures a single route and prints one machine-readable result line the parent parses.
        if(args.Length > 0 && args[0] == "--profile-bulk-load-route")
        {
            await BulkLoadSoak.RunBulkLoadRouteAsync(args).ConfigureAwait(false);

            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
