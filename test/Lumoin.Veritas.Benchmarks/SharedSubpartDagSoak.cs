using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The shared-subpart DAG soak: a deterministic Q1-mix corpus (a
/// shared-subpart <c>partOf</c> DAG, deep <c>supplies</c> transitive chains,
/// <c>owl:sameAs</c> re-resolution cliques, and a flat attribute
/// payload) driven by an append-dominant op stream with episodic retract bursts,
/// measuring at each checkpoint both maintenance strategies side by side: full
/// re-materialization on the current base (<see cref="OwlRlClosure.Compute"/>)
/// and the incrementally-maintained closure
/// (<see cref="OwlRlMaintainedClosure"/>) applying the checkpoint's net base
/// delta.
/// </summary>
/// <remarks>
/// <para>
/// Full remat is the baseline the maintained lane is compared against: per
/// checkpoint it reports base size, derived size, remat wall-clock (median and
/// spread over repeats at small rungs), allocated bytes, and the deterministic
/// proxies the comparand discipline prefers — derived-set size and the total
/// derivation count drawn off the existing inference trace seam, no production
/// instrumentation added. The append vs burst checkpoints are aggregated
/// separately so a rewrite/re-resolution burst's cost is visible against the
/// steady append. At small rungs the naive fixpoint runs alongside and its
/// derived set is asserted equal, so the corpus doubles as a scale-level
/// semi-naive-vs-naive differential.
/// </para>
/// <para>
/// The maintained lane runs one independent <see cref="OwlRlMaintainedClosure"/>
/// per repeat, each applying the same net delta; the median is the reported
/// maintained figure, and every instance's statistics must agree. Each
/// checkpoint's maintained result is asserted consistent-equal and derived-set
/// equal to the remat result, the small rungs additionally pin the delta's own
/// fidelity, and every Apply must report the incremental mode. A per-rung
/// latency-gate block prints, per checkpoint, whether the slowest maintained run beat
/// the fastest remat run, together with the correctness and mode tallies; the
/// block reports figures only and never declares the verdict.
/// </para>
/// <para>
/// Line-oriented output for hand-collation. The default ladder is two rungs;
/// <c>--entities N</c> selects a single rung of a chosen scale (city-scale is
/// reachable by argument, never by default — this is an on-demand harness
/// command).
/// </para>
/// </remarks>
internal static class SharedSubpartDagSoak
{
    /// <summary>The runs per timed checkpoint at a small rung; the median is the reported figure.</summary>
    private const int SmallRungRepeats = 5;

    /// <summary>The runs per timed checkpoint at a large rung, where a single remat is already costly.</summary>
    private const int LargeRungRepeats = 3;

    /// <summary>At or below this entity count a rung runs per-op median timing, the naive differential, and the derivation-count proxy.</summary>
    private const int SmallRungThreshold = 2_000;

    /// <summary>The default ladder's small rung.</summary>
    private const int SmallRungEntities = 1_500;

    /// <summary>The default ladder's larger rung.</summary>
    private const int LargeRungEntities = 20_000;

    /// <summary>The deterministic corpus and op-stream seed; the same seed reproduces the same run across machines.</summary>
    private const int Seed = 20260703;

    /// <summary>The number of burst cycles (each an append checkpoint followed by a retract-burst checkpoint) the op stream runs.</summary>
    private const int BurstCycles = 3;

    /// <summary>The wall-clock budget, in milliseconds, for each of the two warmup loops.</summary>
    private const double WarmupBudgetMilliseconds = 300.0;

    /// <summary>The throwaway maintenance-warmup corpus scale — small enough to warm the pipeline without dominating the budget.</summary>
    private const int WarmupEntities = 300;

    /// <summary>Runs the shared-subpart DAG soak: the default two-rung ladder, or a single rung selected by <c>--entities N</c>.</summary>
    /// <param name="args">The full command line; <c>--entities N</c> selects a single rung.</param>
    /// <returns>The soak's completion.</returns>
    public static async Task RunSharedSubpartDagSoak(string[] args)
    {
        Console.WriteLine($"[subpart-dag] stopwatch frequency={Stopwatch.Frequency:N0} Hz  high-resolution={Stopwatch.IsHighResolution}");
        Console.WriteLine("[subpart-dag] latency gate: 'retract latency beats full-remat at corpus scale' is measured by the per-checkpoint maintained-vs-remat medians and the per-rung latency-gate block below.");

        int? entities = ParseEntities(args);
        if(entities is int single)
        {
            await RunRung(single).ConfigureAwait(false);

            return;
        }

        await RunRung(SmallRungEntities).ConfigureAwait(false);
        await RunRung(LargeRungEntities).ConfigureAwait(false);
    }

    /// <summary>Parses <c>--entities N</c> from the command line, or <see langword="null"/> for the default ladder.</summary>
    /// <param name="args">The full command line.</param>
    /// <returns>The requested single-rung entity count, or <see langword="null"/>.</returns>
    private static int? ParseEntities(string[] args)
    {
        for(int index = 0; index < args.Length - 1; index++)
        {
            if(args[index] == "--entities" && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>Builds one rung's corpus, drives the op stream, measures remat and the maintained lane at every checkpoint, and prints the per-checkpoint table, the append-vs-burst aggregate, and the latency-gate block.</summary>
    /// <param name="entities">The rung's headline scale.</param>
    /// <returns>The rung's completion.</returns>
    private static async Task RunRung(int entities)
    {
        bool smallRung = entities <= SmallRungThreshold;
        int repeats = smallRung ? SmallRungRepeats : LargeRungRepeats;

        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        SharedSubpartDagCorpus corpus = SharedSubpartDagCorpus.Generate(dictionary, terms, entities, Seed);

        Console.WriteLine();
        Console.WriteLine($"[subpart-dag] rung entities={entities:N0} smallRung={smallRung} repeats={repeats} seed={Seed}");
        Console.WriteLine(
            $"[subpart-dag]   corpus: products={corpus.Products.Count:N0} subassemblies={corpus.Subassemblies:N0} leaves={corpus.Leaves:N0} orgs={corpus.Orgs:N0} sameAsCliques={corpus.SameAsBridges.Count:N0}");
        SoakStatistics.ReportGraph(corpus.Snapshot(), $"subpart-dag entities={entities:N0} initial base");
        Console.WriteLine("[subpart-dag]   lanes: canonical (union-find) remat vs rule-based remat vs maintained Apply — the OwlSameAsEquivalence split");

        List<CheckpointResult> results = [];
        Random random = new(Seed);
        List<EncodedTriple> initialBase = corpus.Snapshot();

        //Warm the closure's tiered JIT so the first timed checkpoint reports
        //steady-state remat rather than method compilation, bounded by a time
        //budget so a large rung's costly remat does not over-warm.
        long warmupStart = Stopwatch.GetTimestamp();
        for(int warmup = 0; warmup < 25 && Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < WarmupBudgetMilliseconds; warmup++)
        {
            _ = OwlRlClosure.Compute(initialBase, terms, oracle);
        }

        //Warm the canonical closure and its expansion on the same budget so
        //the first timed canonical checkpoint reports steady-state rather
        //than the union-find and expansion methods' first compilation.
        long canonicalWarmupStart = Stopwatch.GetTimestamp();
        for(int warmup = 0; warmup < 25 && Stopwatch.GetElapsedTime(canonicalWarmupStart).TotalMilliseconds < WarmupBudgetMilliseconds; warmup++)
        {
            OwlRlCanonicalResult warmCanonical = OwlRlCanonicalClosure.Compute(initialBase, terms, oracle);
            _ = OwlRlCanonicalClosure.ExpandToMaterialization(warmCanonical, terms);
        }

        WarmMaintainedPipeline();

        //Independent maintained closures, one per repeat, all built from the
        //same initial base — the from-scratch build the maintained Apply latency
        //is measured against.
        OwlRlMaintainedClosure[] instances = new OwlRlMaintainedClosure[repeats];
        double[] constructionTimes = new double[repeats];
        for(int i = 0; i < repeats; i++)
        {
            long start = Stopwatch.GetTimestamp();
            instances[i] = new OwlRlMaintainedClosure(initialBase, terms, oracle);
            constructionTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(constructionTimes);
        Console.WriteLine($"[subpart-dag]   maintained construction median={constructionTimes[repeats / 2]:F2} ms over {repeats} instances (a from-scratch build, remat-shaped)");

        //A separate profile instance over the same initial base, never counted
        //into the construction median and never timed: the phase-attribution
        //window runs on it alone, so diagnostic overhead never lands in a
        //reported wall-clock, and it stays in lockstep with instance 0 by
        //applying every checkpoint's net delta.
        OwlRlMaintainedClosure profileInstance = new(initialBase, terms, oracle);

        List<EncodedTriple> previousSnapshot = initialBase;
        results.Add(Measure(initialBase, terms, oracle, "initial", CheckpointKind.Initial, repeats, smallRung, null, instances, profileInstance, previousSnapshot));

        for(int cycle = 0; cycle < BurstCycles; cycle++)
        {
            SharedSubpartDagDelta appendDelta = corpus.AppendBatch(random);
            List<EncodedTriple> afterAppend = corpus.Snapshot();
            results.Add(Measure(afterAppend, terms, oracle, $"append#{cycle + 1}", CheckpointKind.Append, repeats, smallRung, appendDelta, instances, profileInstance, previousSnapshot));
            previousSnapshot = afterAppend;

            SharedSubpartDagDelta burstDelta = corpus.RetractBurst(random);
            List<EncodedTriple> afterBurst = corpus.Snapshot();
            results.Add(Measure(afterBurst, terms, oracle, $"burst#{cycle + 1}", CheckpointKind.Burst, repeats, smallRung, burstDelta, instances, profileInstance, previousSnapshot));
            previousSnapshot = afterBurst;
        }

        ReportSummary(entities, results);
        ReportLatencyGate(results);

        //The production lane runs AFTER the engine-level lanes, over its own
        //independent corpus and reasoned datasets, so it never perturbs the
        //remat/maintained/canonical medians reported above. It drives the same
        //deterministic op stream through the production commit path and reads the
        //gate letter off the full per-checkpoint commit cost.
        await RunProductionLane(entities, results, smallRung, repeats).ConfigureAwait(false);
    }

    /// <summary>Warms the maintenance pipeline — the overdelete families, the head-bound rederive matcher, and index removal — on a throwaway corpus so first-Apply compilation never lands inside a timed checkpoint.</summary>
    private static void WarmMaintainedPipeline()
    {
        TermDictionary warmDictionary = new();
        OwlRlTerms warmTerms = new(warmDictionary);
        OwlRlDatatypeOracle warmOracle = OwlRlDatatypeOracles.FromDictionary(warmDictionary);
        SharedSubpartDagCorpus warmCorpus = SharedSubpartDagCorpus.Generate(warmDictionary, warmTerms, WarmupEntities, Seed);
        OwlRlMaintainedClosure warmClosure = new(warmCorpus.Snapshot(), warmTerms, warmOracle);
        Random warmRandom = new(Seed);

        long warmupStart = Stopwatch.GetTimestamp();
        while(Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < WarmupBudgetMilliseconds)
        {
            SharedSubpartDagDelta appended = warmCorpus.AppendBatch(warmRandom);
            _ = warmClosure.Apply(appended.Added, appended.Retracted);

            SharedSubpartDagDelta burst = warmCorpus.RetractBurst(warmRandom);
            _ = warmClosure.Apply(burst.Added, burst.Retracted);
        }
    }

    /// <summary>Measures one checkpoint: the current base's full-remat cost and the maintained-Apply cost over <paramref name="repeats"/> runs, plus the deterministic proxies, the correctness assertions, and, at small rungs, the naive differential and the delta-fidelity pin.</summary>
    /// <param name="baseTriples">The current base — the post-op snapshot the remat closes over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="label">The checkpoint label.</param>
    /// <param name="kind">Whether this checkpoint follows the initial base, an append, or a retract burst.</param>
    /// <param name="repeats">The timed-run count.</param>
    /// <param name="smallRung">Whether to run the naive differential, the derivation-count proxy, and the delta-fidelity pin.</param>
    /// <param name="delta">The op's net base delta, or <see langword="null"/> at the initial checkpoint where no Apply runs.</param>
    /// <param name="instances">The maintained closures, one per repeat, advanced by this checkpoint's Apply.</param>
    /// <param name="profileInstance">The separate, never-timed maintained closure the phase-attribution window runs on, advanced in lockstep by this checkpoint's Apply.</param>
    /// <param name="previousSnapshot">The base before this op — the delta-fidelity pin's left operand.</param>
    /// <returns>The checkpoint's measured result.</returns>
    private static CheckpointResult Measure(
        List<EncodedTriple> baseTriples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        string label,
        CheckpointKind kind,
        int repeats,
        bool smallRung,
        SharedSubpartDagDelta? delta,
        OwlRlMaintainedClosure[] instances,
        OwlRlMaintainedClosure profileInstance,
        List<EncodedTriple> previousSnapshot)
    {
        double[] times = new double[repeats];
        long allocated = 0;
        int derivedCount = 0;
        bool consistent = true;
        HashSet<EncodedTriple> rematDerived = [];
        for(int run = 0; run < repeats; run++)
        {
            long start = Stopwatch.GetTimestamp();
            long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            OwlRlResult result = OwlRlClosure.Compute(baseTriples, terms, oracle);
            allocated = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
            times[run] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            derivedCount = result.Derived.Count;
            consistent = result.IsConsistent;
            if(delta.HasValue && run == repeats - 1)
            {
                rematDerived = [.. result.Derived];
            }
        }

        Array.Sort(times);
        double median = times[repeats / 2];

        //Deterministic derivation-count proxy: the total rule firings drawn off
        //the inference trace seam in one untimed pass, so the trace overhead
        //never lands in the reported wall-clock. Round count is not exposed
        //without production instrumentation, which the comparand discipline
        //forbids; derived-set size and this firing count are the deterministic
        //proxies used instead.
        long derivations = -1;
        if(smallRung)
        {
            derivations = CountDerivations(baseTriples, terms, oracle);

            OwlRlResult naive = OwlRlClosure.ComputeNaive(baseTriples, terms, oracle);
            OwlRlResult semiNaive = OwlRlClosure.Compute(baseTriples, terms, oracle);
            HashSet<EncodedTriple> naiveDerived = [.. naive.Derived];
            bool match = naiveDerived.SetEquals(semiNaive.Derived) && naive.IsConsistent == semiNaive.IsConsistent;
            Console.WriteLine($"[subpart-dag]   {label,-10} differential: {(match ? "MATCH" : "MISMATCH")}");
        }

        string derivationsText = derivations < 0 ? "n/a" : derivations.ToString("N0", CultureInfo.InvariantCulture);
        Console.WriteLine(
            $"[subpart-dag]   {label,-10} base={baseTriples.Count,9:N0} derived={derivedCount,9:N0} remat median={median,9:F2} ms (min {times[0]:F2}, max {times[^1]:F2}) alloc={allocated / (1024.0 * 1024.0),8:F1} MB firings={derivationsText} consistent={consistent}");

        bool maintainedMeasured = delta.HasValue;
        double maintainedMedian = 0.0;
        double maintainedMin = 0.0;
        double maintainedMax = 0.0;
        long maintainedAllocated = 0;
        OwlRlMaintenanceStatistics maintainedStatistics = default;
        bool correctnessOk = true;
        bool modeIncremental = true;

        if(delta is SharedSubpartDagDelta netDelta)
        {
            double[] maintainedTimes = new double[repeats];
            bool maintainedConsistent = true;
            IReadOnlyCollection<EncodedTriple> maintainedDerived = Array.Empty<EncodedTriple>();
            for(int run = 0; run < repeats; run++)
            {
                long start = Stopwatch.GetTimestamp();
                long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
                OwlRlResult applied = instances[run].Apply(netDelta.Added, netDelta.Retracted);
                long allocAfter = GC.GetTotalAllocatedBytes(precise: true);
                maintainedTimes[run] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                OwlRlMaintenanceStatistics runStatistics = instances[run].Statistics;
                if(run == 0)
                {
                    maintainedAllocated = allocAfter - allocBefore;
                    maintainedStatistics = runStatistics;
                    maintainedConsistent = applied.IsConsistent;
                    maintainedDerived = applied.Derived;
                }
                else if(!runStatistics.Equals(maintainedStatistics))
                {
                    Console.WriteLine($"[subpart-dag] MISMATCH {label}: instance {run} statistics {runStatistics} differ from instance 0 {maintainedStatistics}");
                    correctnessOk = false;
                }
            }

            Array.Sort(maintainedTimes);
            maintainedMedian = maintainedTimes[repeats / 2];
            maintainedMin = maintainedTimes[0];
            maintainedMax = maintainedTimes[^1];

            modeIncremental = maintainedStatistics.Mode == OwlRlMaintenanceMode.Incremental;
            if(!modeIncremental)
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: maintained mode {maintainedStatistics.Mode} is not Incremental — a rebuild silently measures remat against remat");
                correctnessOk = false;
            }

            if(maintainedConsistent != consistent)
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: maintained consistency {maintainedConsistent} != remat consistency {consistent}");
                correctnessOk = false;
            }

            if(!rematDerived.SetEquals(maintainedDerived))
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: maintained derived set diverges from remat (maintained {maintainedDerived.Count}, remat {rematDerived.Count})");
                correctnessOk = false;
            }

            if(smallRung)
            {
                HashSet<EncodedTriple> reconstructed = [.. previousSnapshot];
                reconstructed.ExceptWith(netDelta.Retracted);
                reconstructed.UnionWith(netDelta.Added);
                if(!reconstructed.SetEquals(baseTriples))
                {
                    Console.WriteLine($"[subpart-dag] MISMATCH {label}: delta fidelity (previous \\ retracted) ∪ added != current base");
                    correctnessOk = false;
                }
            }

            double speedup = maintainedMedian > 0.0 ? median / maintainedMedian : 0.0;
            int marked = maintainedStatistics.OverdeleteMarked;
            int trulyLost = maintainedStatistics.OverdeleteMarked - maintainedStatistics.RestoredTotal;
            Console.WriteLine(
                $"[subpart-dag]   {label,-10} maintained median={maintainedMedian,9:F2} ms (min {maintainedMin:F2}, max {maintainedMax:F2}) alloc={maintainedAllocated / (1024.0 * 1024.0),8:F1} MB speedup=x{speedup:F1} marked={marked} trulyLost={trulyLost} deletionRounds={maintainedStatistics.DeletionRounds} rederived={maintainedStatistics.DirectlyRederived} restored={maintainedStatistics.RestoredTotal} insertRounds={maintainedStatistics.InsertRounds} reFires={maintainedStatistics.ChoiceOwnerReFires} demote={maintainedStatistics.BaseDemotions} promote={maintainedStatistics.BasePromotions} mode={maintainedStatistics.Mode}");

            //Phase attribution runs on the never-timed profile instance so the
            //diagnostic overhead never lands in a reported wall-clock. The engine
            //is deterministic, so the profile instance's statistics and mode must
            //match instance 0's; the instance stays in lockstep by applying this
            //same net delta.
            OwlRlMaintenanceInstrumentation.Enable();
            OwlRlMaintenanceInstrumentationReport profileReport;
            double profileApplyMilliseconds;
            OwlRlMaintenanceStatistics profileStatistics;
            try
            {
                long profileStart = Stopwatch.GetTimestamp();
                _ = profileInstance.Apply(netDelta.Added, netDelta.Retracted);
                profileApplyMilliseconds = Stopwatch.GetElapsedTime(profileStart).TotalMilliseconds;
                profileReport = OwlRlMaintenanceInstrumentation.Snapshot();
                profileStatistics = profileInstance.Statistics;
            }
            finally
            {
                OwlRlMaintenanceInstrumentation.Disable();
            }

            if(!profileStatistics.Equals(maintainedStatistics))
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: profile instance statistics {profileStatistics} differ from instance 0 {maintainedStatistics}");
                correctnessOk = false;
            }

            if(profileStatistics.Mode != OwlRlMaintenanceMode.Incremental)
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: profile instance mode {profileStatistics.Mode} is not Incremental — a rebuild would attribute rebuild phases");
                correctnessOk = false;
            }

            PrintPhaseBlock(label, kind, profileReport, profileApplyMilliseconds, marked);
        }
        else
        {
            Console.WriteLine($"[subpart-dag]   {label,-10} maintained n/a (the initial base carries no delta to apply)");
        }

        MeasureCanonicalLane(baseTriples, terms, oracle, label, repeats, smallRung);

        return new CheckpointResult(
            label,
            kind,
            baseTriples.Count,
            derivedCount,
            median,
            times[0],
            times[^1],
            allocated,
            derivations,
            maintainedMeasured,
            maintainedMedian,
            maintainedMin,
            maintainedMax,
            maintainedAllocated,
            maintainedStatistics,
            correctnessOk,
            modeIncremental);
    }

    /// <summary>
    /// Measures the canonical (union-find) remat lane at one checkpoint, entirely outside every existing timed region: the canonical closure's compute cost (<see cref="OwlRlCanonicalClosure.Compute"/>) and, separately, its expansion-to-materialization cost (<see cref="OwlRlCanonicalClosure.ExpandToMaterialization"/> — the cost a serving swap would actually pay), each over <paramref name="repeats"/> runs in the same median-plus-spread discipline the rule-based remat lane uses, reporting the canonical derived-row and clique counts. At small rungs it asserts the expansion set-equals the rule-based remat's materialization, both derived set and consistency verdict, and reports the differential without throwing.
    /// </summary>
    /// <param name="baseTriples">The current base — the identical input triples the rule-based remat closes over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle, matching the rule-based remat lane.</param>
    /// <param name="label">The checkpoint label.</param>
    /// <param name="repeats">The timed-run count, matching the rule-based remat lane.</param>
    /// <param name="smallRung">Whether to run the canonical-vs-rule-based expansion differential.</param>
    private static void MeasureCanonicalLane(
        List<EncodedTriple> baseTriples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        string label,
        int repeats,
        bool smallRung)
    {
        double[] computeTimes = new double[repeats];
        double[] expandTimes = new double[repeats];
        long computeAllocated = 0;
        long expandAllocated = 0;
        int derivedCount = 0;
        int cliqueCount = 0;
        int materializedCount = 0;
        bool consistent = true;
        IReadOnlyCollection<EncodedTriple> materialization = Array.Empty<EncodedTriple>();
        for(int run = 0; run < repeats; run++)
        {
            long computeStart = Stopwatch.GetTimestamp();
            long computeAllocBefore = GC.GetTotalAllocatedBytes(precise: true);
            OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(baseTriples, terms, oracle);
            computeAllocated = GC.GetTotalAllocatedBytes(precise: true) - computeAllocBefore;
            computeTimes[run] = Stopwatch.GetElapsedTime(computeStart).TotalMilliseconds;

            long expandStart = Stopwatch.GetTimestamp();
            long expandAllocBefore = GC.GetTotalAllocatedBytes(precise: true);
            materialization = OwlRlCanonicalClosure.ExpandToMaterialization(canonical, terms);
            expandAllocated = GC.GetTotalAllocatedBytes(precise: true) - expandAllocBefore;
            expandTimes[run] = Stopwatch.GetElapsedTime(expandStart).TotalMilliseconds;

            derivedCount = canonical.Result.Derived.Count;
            cliqueCount = canonical.Equivalence.CliqueCount;
            materializedCount = materialization.Count;
            consistent = canonical.Result.IsConsistent;
        }

        Array.Sort(computeTimes);
        Array.Sort(expandTimes);
        double computeMedian = computeTimes[repeats / 2];
        double expandMedian = expandTimes[repeats / 2];
        Console.WriteLine(
            $"[subpart-dag]   {label,-10} canonical remat median={computeMedian,9:F2} ms (min {computeTimes[0]:F2}, max {computeTimes[^1]:F2}) alloc={computeAllocated / (1024.0 * 1024.0),8:F1} MB derived={derivedCount,9:N0} cliques={cliqueCount:N0} consistent={consistent}");
        Console.WriteLine(
            $"[subpart-dag]   {label,-10} canonical +expand median={expandMedian,9:F2} ms (min {expandTimes[0]:F2}, max {expandTimes[^1]:F2}) alloc={expandAllocated / (1024.0 * 1024.0),8:F1} MB compute+expand={computeMedian + expandMedian:F2} ms materialized={materializedCount:N0} (the cost a serving swap pays)");

        if(smallRung)
        {
            OwlRlResult ruleBased = OwlRlClosure.Compute(baseTriples, terms, oracle);
            HashSet<EncodedTriple> ruleMaterialization = [.. baseTriples, .. ruleBased.Derived];
            HashSet<EncodedTriple> expanded = [.. materialization];
            bool derivedMatch = ruleMaterialization.SetEquals(expanded);
            bool consistencyMatch = ruleBased.IsConsistent == consistent;
            Console.WriteLine($"[subpart-dag]   {label,-10} canonical differential: {(derivedMatch && consistencyMatch ? "MATCH" : "MISMATCH")}");
            if(!derivedMatch || !consistencyMatch)
            {
                Console.WriteLine(
                    $"[subpart-dag] MISMATCH {label}: canonical expansion diverges from rule-based materialization (expanded {expanded.Count}, rule-based {ruleMaterialization.Count}, consistency canonical={consistent} rule-based={ruleBased.IsConsistent})");
            }
        }
    }

    /// <summary>Counts the total derivations the closure fires over a base, via the existing inference trace seam.</summary>
    /// <param name="baseTriples">The base to close over.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <returns>The number of trace events, one per derivation.</returns>
    private static long CountDerivations(List<EncodedTriple> baseTriples, OwlRlTerms terms, OwlRlDatatypeOracle oracle)
    {
        long count = 0;
        OwlRlClosure.Compute(
            baseTriples,
            terms,
            oracle,
            (in InferenceTraceEvent _) => count++,
            TimeProvider.System);

        return count;
    }

    /// <summary>The maintenance phases in print order; each of the three nested child phases immediately follows its parent.</summary>
    private static readonly OwlRlMaintenancePhase[] PhasePrintOrder =
    [
        OwlRlMaintenancePhase.OverdeleteGrouping,
        OwlRlMaintenancePhase.OwnerMarking,
        OwlRlMaintenancePhase.OverdeleteEquality,
        OwlRlMaintenancePhase.OverdeleteProperties,
        OwlRlMaintenancePhase.OverdeleteCharacteristicData,
        OwlRlMaintenancePhase.OverdeleteClasses,
        OwlRlMaintenancePhase.OverdeleteMaxPairs,
        OwlRlMaintenancePhase.OverdeleteClassAxioms,
        OwlRlMaintenancePhase.OverdeleteSchema,
        OwlRlMaintenancePhase.PhysicalRemoval,
        OwlRlMaintenancePhase.BaseAdmission,
        OwlRlMaintenancePhase.Rederive,
        OwlRlMaintenancePhase.RederiveEqRep,
        OwlRlMaintenancePhase.OwnerReFire,
        OwlRlMaintenancePhase.InsertRounds,
    ];

    /// <summary>Prints the per-checkpoint phase-attribution block: a header line with the Apply wall-clock and the attributed total, then one line per nonzero-count phase with its share of the attributed total, children indented under their parent, and the per-marked rate on burst checkpoints.</summary>
    /// <param name="label">The checkpoint label.</param>
    /// <param name="kind">The checkpoint kind; burst checkpoints add the per-marked rate.</param>
    /// <param name="report">The phase-attribution snapshot of the profile instance's Apply.</param>
    /// <param name="applyMilliseconds">The profile instance's Apply wall-clock.</param>
    /// <param name="marked">The facts marked after the overdelete fixpoint - the per-marked rate's denominator.</param>
    private static void PrintPhaseBlock(string label, CheckpointKind kind, OwlRlMaintenanceInstrumentationReport report, double applyMilliseconds, int marked)
    {
        double attributed = report.TotalAttributedMilliseconds;
        double attributedShareOfApply = applyMilliseconds > 0.0 ? attributed / applyMilliseconds * 100.0 : 0.0;
        Console.WriteLine(FormattableString.Invariant(
            $"[subpart-dag]   {label} profile: apply={applyMilliseconds:F2} ms attributed={attributed:F2} ms ({attributedShareOfApply:F1}% of apply)"));

        foreach(OwlRlMaintenancePhase phase in PhasePrintOrder)
        {
            long count = report.Count(phase);
            if(count == 0)
            {
                continue;
            }

            double phaseMilliseconds = report.Milliseconds(phase);
            double share = attributed > 0.0 ? phaseMilliseconds / attributed * 100.0 : 0.0;
            OwlRlMaintenancePhase? parent = ParentOf(phase);
            string indent = parent is null ? "    " : "      ";
            string line = FormattableString.Invariant(
                $"[subpart-dag] {indent}phase {PhaseName(phase)} ms={phaseMilliseconds:F2} count={count} share={share:F1}%");
            if(parent is OwlRlMaintenancePhase parentPhase)
            {
                line += FormattableString.Invariant($" within={PhaseName(parentPhase)}");
            }

            if(kind == CheckpointKind.Burst && marked > 0)
            {
                double perMarked = phaseMilliseconds * 1000.0 / marked;
                line += FormattableString.Invariant($" perMarked={perMarked:F1} us");
            }

            Console.WriteLine(line);
        }
    }

    /// <summary>The parent of a nested child phase, or <see langword="null"/> for a top-level phase.</summary>
    /// <param name="phase">The phase to classify.</param>
    /// <returns>The parent phase when <paramref name="phase"/> is one of the three nested children, otherwise <see langword="null"/>.</returns>
    private static OwlRlMaintenancePhase? ParentOf(OwlRlMaintenancePhase phase)
    {
        return phase switch
        {
            OwlRlMaintenancePhase.OverdeleteCharacteristicData => OwlRlMaintenancePhase.OverdeleteProperties,
            OwlRlMaintenancePhase.OverdeleteMaxPairs => OwlRlMaintenancePhase.OverdeleteClasses,
            OwlRlMaintenancePhase.RederiveEqRep => OwlRlMaintenancePhase.Rederive,
            _ => null,
        };
    }

    /// <summary>The stable, culture-invariant display name of a maintenance phase.</summary>
    /// <param name="phase">The phase to name.</param>
    /// <returns>The phase's display name.</returns>
    private static string PhaseName(OwlRlMaintenancePhase phase)
    {
        return phase switch
        {
            OwlRlMaintenancePhase.OverdeleteGrouping => "OverdeleteGrouping",
            OwlRlMaintenancePhase.OwnerMarking => "OwnerMarking",
            OwlRlMaintenancePhase.OverdeleteEquality => "OverdeleteEquality",
            OwlRlMaintenancePhase.OverdeleteProperties => "OverdeleteProperties",
            OwlRlMaintenancePhase.OverdeleteCharacteristicData => "OverdeleteCharacteristicData",
            OwlRlMaintenancePhase.OverdeleteClasses => "OverdeleteClasses",
            OwlRlMaintenancePhase.OverdeleteMaxPairs => "OverdeleteMaxPairs",
            OwlRlMaintenancePhase.OverdeleteClassAxioms => "OverdeleteClassAxioms",
            OwlRlMaintenancePhase.OverdeleteSchema => "OverdeleteSchema",
            OwlRlMaintenancePhase.PhysicalRemoval => "PhysicalRemoval",
            OwlRlMaintenancePhase.BaseAdmission => "BaseAdmission",
            OwlRlMaintenancePhase.Rederive => "Rederive",
            OwlRlMaintenancePhase.RederiveEqRep => "RederiveEqRep",
            OwlRlMaintenancePhase.OwnerReFire => "OwnerReFire",
            OwlRlMaintenancePhase.InsertRounds => "InsertRounds",
            _ => phase.ToString(),
        };
    }

    /// <summary>Prints the per-checkpoint table and the append-vs-burst aggregate for a rung.</summary>
    /// <param name="entities">The rung's headline scale.</param>
    /// <param name="results">The rung's checkpoint results, in op order.</param>
    private static void ReportSummary(int entities, List<CheckpointResult> results)
    {
        Console.WriteLine($"[subpart-dag]   rung entities={entities:N0} summary:");
        Console.WriteLine($"[subpart-dag]     {"checkpoint",-10} {"kind",-8} {"base",10} {"derived",10} {"remat ms",10} {"min",8} {"max",8} {"maint ms",10} {"speedup",9}");
        foreach(CheckpointResult result in results)
        {
            string maintText = result.MaintainedMeasured ? result.MaintainedMedianMilliseconds.ToString("F2", CultureInfo.InvariantCulture) : "n/a";
            string speedupText = result.MaintainedMeasured && result.MaintainedMedianMilliseconds > 0.0
                ? "x" + (result.MedianMilliseconds / result.MaintainedMedianMilliseconds).ToString("F1", CultureInfo.InvariantCulture)
                : "n/a";
            Console.WriteLine(
                $"[subpart-dag]     {result.Label,-10} {result.Kind,-8} {result.BaseSize,10:N0} {result.DerivedSize,10:N0} {result.MedianMilliseconds,10:F2} {result.MinMilliseconds,8:F2} {result.MaxMilliseconds,8:F2} {maintText,10} {speedupText,9}");
        }

        (double appendRemat, double appendMaint, int appendCount) = MedianOf(results, CheckpointKind.Append);
        (double burstRemat, double burstMaint, int burstCount) = MedianOf(results, CheckpointKind.Burst);
        Console.WriteLine(
            $"[subpart-dag]     append-vs-burst: append checkpoints={appendCount} median remat={appendRemat:F2} ms maintained={appendMaint:F2} ms | burst checkpoints={burstCount} median remat={burstRemat:F2} ms maintained={burstMaint:F2} ms");
    }

    /// <summary>Prints the per-rung latency-gate block: per-kind pass tallies against the spread-free criterion (the slowest maintained run beats the fastest remat run), the correctness and mode flags, and one line per failing checkpoint.</summary>
    /// <param name="results">The rung's checkpoint results, in op order.</param>
    private static void ReportLatencyGate(List<CheckpointResult> results)
    {
        int appendPass = 0;
        int appendTotal = 0;
        int burstPass = 0;
        int burstTotal = 0;
        bool correctnessOk = true;
        bool modesIncremental = true;
        List<CheckpointResult> failing = [];

        foreach(CheckpointResult result in results)
        {
            if(!result.MaintainedMeasured)
            {
                continue;
            }

            correctnessOk = correctnessOk && result.CorrectnessOk;
            modesIncremental = modesIncremental && result.ModeIncremental;
            bool pass = result.MaintainedMaxMilliseconds < result.MinMilliseconds;

            if(result.Kind == CheckpointKind.Append)
            {
                appendTotal++;
                if(pass)
                {
                    appendPass++;
                }
                else
                {
                    failing.Add(result);
                }
            }
            else if(result.Kind == CheckpointKind.Burst)
            {
                burstTotal++;
                if(pass)
                {
                    burstPass++;
                }
                else
                {
                    failing.Add(result);
                }
            }
        }

        Console.WriteLine(
            $"[subpart-dag]   latency-gate: burst {burstPass}/{burstTotal} pass, append {appendPass}/{appendTotal} pass, correctness {(correctnessOk ? "OK" : "FAILED")}, modes {(modesIncremental ? "all Incremental" : "NOT all Incremental")}");
        foreach(CheckpointResult fail in failing)
        {
            Console.WriteLine(
                $"[subpart-dag]     latency-gate FAIL {fail.Label}: maintained max={fail.MaintainedMaxMilliseconds:F2} ms not < remat min={fail.MinMilliseconds:F2} ms");
        }
    }

    /// <summary>
    /// Drives the SAME deterministic op stream through the PRODUCTION commit path — a reasoned
    /// <see cref="MutableSparqlDataset"/> whose maintenance delegate is wired to a real
    /// <see cref="ReasoningMaintenance"/> exactly as the Database layer composes it (§7.7) — measuring the full
    /// per-checkpoint commit cost (<see cref="DatasetEditSession.OpenSessionAsync"/> through
    /// <see cref="DatasetEditSession.CommitAsync"/> return), asserting the served store equals the branched oracle
    /// (base ∪ remat.Derived when consistent), certifying every eligible commit is <c>Incremental</c> with counters
    /// byte-identical to the engine-level maintained lane, and printing the PRODUCTION latency-gate letter (per
    /// checkpoint, production commit-cost MAX ≤ half the same-run rule-remat MIN). It regenerates its own corpus at
    /// the same seed, so its deltas match the engine-level lane's byte for byte, and runs entirely outside every
    /// existing timed region.
    /// </summary>
    /// <param name="entities">The rung's headline scale.</param>
    /// <param name="engineResults">The engine-level lane's per-checkpoint results, the counter-identity comparand.</param>
    /// <param name="smallRung">Whether to run the served-correctness teeth over every repeat (the large rung caps them to instance 0).</param>
    /// <param name="repeats">The timed-run count, matching the engine-level lane.</param>
    /// <returns>The production lane's completion.</returns>
    private static async Task RunProductionLane(int entities, List<CheckpointResult> engineResults, bool smallRung, int repeats)
    {
        Console.WriteLine();
        Console.WriteLine($"[subpart-dag]   PRODUCTION lane entities={entities:N0} repeats={repeats}: the SAME op stream through MutableSparqlDataset/DatasetEditSession with a real ReasoningMaintenance wired (production commit path minus SPARQL text parsing)");

        await WarmProductionCommitPipeline().ConfigureAwait(false);

        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        SharedSubpartDagCorpus corpus = SharedSubpartDagCorpus.Generate(dictionary, terms, entities, Seed);
        Random random = new(Seed);
        List<EncodedTriple> initialBase = corpus.Snapshot();
        ReasoningPolicy policy = ReasoningConfiguration.Default.Policy;

        //Independent reasoned datasets, one per repeat, all built from the same
        //initial base — the construction cost is one remat per instance and is
        //never timed. The whole op stream is replayed on each instance, so they
        //stay in lockstep and their counters are byte-identical.
        ProductionInstance[] instances = new ProductionInstance[repeats];
        double[] constructionTimes = new double[repeats];
        for(int i = 0; i < repeats; i++)
        {
            long start = Stopwatch.GetTimestamp();
            instances[i] = await ProductionInstance.CreateAsync(dictionary, initialBase, policy, CancellationToken.None).ConfigureAwait(false);
            constructionTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(constructionTimes);
        Console.WriteLine($"[subpart-dag]   PRODUCTION construction median={constructionTimes[repeats / 2]:F2} ms over {repeats} reasoned datasets (a remat-shaped open, outside the timed commit regions)");

        List<ProductionCheckpointResult> productionResults = [];
        int resultIndex = 1;
        for(int cycle = 0; cycle < BurstCycles; cycle++)
        {
            SharedSubpartDagDelta appendDelta = corpus.AppendBatch(random);
            productionResults.Add(await MeasureProductionCheckpoint(
                corpus.Snapshot(), terms, oracle, $"append#{cycle + 1}", CheckpointKind.Append, appendDelta, instances, engineResults[resultIndex++], repeats, smallRung).ConfigureAwait(false));

            SharedSubpartDagDelta burstDelta = corpus.RetractBurst(random);
            productionResults.Add(await MeasureProductionCheckpoint(
                corpus.Snapshot(), terms, oracle, $"burst#{cycle + 1}", CheckpointKind.Burst, burstDelta, instances, engineResults[resultIndex++], repeats, smallRung).ConfigureAwait(false));
        }

        //The remat-production comparand lane runs AFTER the maintained-production
        //lane, over its own independent corpus and reasoned-shaped datasets at the
        //same seed, so it never perturbs the maintained-production timed regions. It
        //drives the SAME op stream through the SAME MutableSparqlDataset commit path
        //but with a remat-based maintenance delegate — the SYMMETRIC baseline the
        //letter reads against (both paths pay the identical dataset commit machinery;
        //only the maintenance strategy differs).
        List<RematProductionCheckpointResult> rematResults =
            await RunRematProductionLane(entities, productionResults, smallRung, repeats).ConfigureAwait(false);

        ReportProductionLatencyGate(productionResults, rematResults);

        await RunFacadeSmoke().ConfigureAwait(false);
    }

    /// <summary>
    /// Drives the SAME deterministic op stream through the SAME production commit path as
    /// <see cref="RunProductionLane"/>, but with a REMAT-BASED maintenance delegate: on each commit it recomputes
    /// the full closure over the post-op asserted base (<see cref="OwlRlClosure.Compute"/> — the same call the
    /// engine-level remat lane times) and computes the served delta as the setdiff between the new served target
    /// (base ∪ Derived when consistent) and the previous served store it tracks, then returns it as a
    /// <see cref="MaintainedCommitDelta"/> so the identical <c>CommitMaintainedAsync</c> machinery (mutex, served
    /// ApplyDelta, state id, journal, Publish) executes. It regenerates its own corpus at the same seed so its
    /// deltas match the maintained-production lane's byte for byte, and runs entirely outside every existing timed
    /// region — the symmetric baseline the letter reads the maintained-production commit against.
    /// </summary>
    /// <param name="entities">The rung's headline scale.</param>
    /// <param name="productionResults">The maintained-production lane's per-checkpoint results — the same-run comparand for the speedup and the symmetric letter.</param>
    /// <param name="smallRung">Whether to run the served-correctness teeth over every repeat (the large rung caps them to instance 0).</param>
    /// <param name="repeats">The timed-run count, matching the maintained-production lane.</param>
    /// <returns>The remat-production lane's per-checkpoint results, in op order.</returns>
    private static async Task<List<RematProductionCheckpointResult>> RunRematProductionLane(int entities, List<ProductionCheckpointResult> productionResults, bool smallRung, int repeats)
    {
        Console.WriteLine();
        Console.WriteLine($"[subpart-dag]   REMAT-PRODUCTION lane entities={entities:N0} repeats={repeats}: the SAME op stream through the SAME MutableSparqlDataset commit path with a remat-based maintenance delegate (full closure recompute + served rebuild per commit) — the symmetric baseline for the letter");

        await WarmRematProductionCommitPipeline().ConfigureAwait(false);

        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        SharedSubpartDagCorpus corpus = SharedSubpartDagCorpus.Generate(dictionary, terms, entities, Seed);
        Random random = new(Seed);
        List<EncodedTriple> initialBase = corpus.Snapshot();

        //Independent remat-serving datasets, one per repeat, all built from the same
        //initial base — the construction cost is one remat per instance and is never
        //timed. The whole op stream is replayed on each instance so they stay in
        //lockstep and their served stores agree.
        RematProductionInstance[] instances = new RematProductionInstance[repeats];
        double[] constructionTimes = new double[repeats];
        for(int i = 0; i < repeats; i++)
        {
            long start = Stopwatch.GetTimestamp();
            instances[i] = await RematProductionInstance.CreateAsync(dictionary, terms, oracle, initialBase, CancellationToken.None).ConfigureAwait(false);
            constructionTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(constructionTimes);
        Console.WriteLine($"[subpart-dag]   REMAT-PRODUCTION construction median={constructionTimes[repeats / 2]:F2} ms over {repeats} remat-serving datasets (a remat-shaped open, outside the timed commit regions)");

        List<RematProductionCheckpointResult> rematResults = [];
        int resultIndex = 0;
        for(int cycle = 0; cycle < BurstCycles; cycle++)
        {
            SharedSubpartDagDelta appendDelta = corpus.AppendBatch(random);
            rematResults.Add(await MeasureRematProductionCheckpoint(
                corpus.Snapshot(), terms, oracle, $"append#{cycle + 1}", CheckpointKind.Append, appendDelta, instances, productionResults[resultIndex++], repeats, smallRung).ConfigureAwait(false));

            SharedSubpartDagDelta burstDelta = corpus.RetractBurst(random);
            rematResults.Add(await MeasureRematProductionCheckpoint(
                corpus.Snapshot(), terms, oracle, $"burst#{cycle + 1}", CheckpointKind.Burst, burstDelta, instances, productionResults[resultIndex++], repeats, smallRung).ConfigureAwait(false));
        }

        return rematResults;
    }

    /// <summary>Measures one remat-production checkpoint: the full commit cost over <paramref name="repeats"/> remat-serving datasets, the served-store correctness teeth, and the speedup of the maintained-production commit against this remat-production commit at the same checkpoint.</summary>
    /// <param name="baseTriples">The post-op asserted base the remat closes over and the served store must reproduce the closure of.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="label">The checkpoint label.</param>
    /// <param name="kind">Whether this checkpoint follows an append or a retract burst.</param>
    /// <param name="delta">The op's net base delta, applied as one session commit per instance.</param>
    /// <param name="instances">The remat-serving datasets, one per repeat, advanced by this checkpoint's commit.</param>
    /// <param name="productionResult">The maintained-production lane's result at this checkpoint — the same-run speedup and symmetric-letter comparand.</param>
    /// <param name="repeats">The timed-run count.</param>
    /// <param name="smallRung">Whether to run the served-correctness teeth over every repeat rather than instance 0 only.</param>
    /// <returns>The checkpoint's measured remat-production result.</returns>
    private static async Task<RematProductionCheckpointResult> MeasureRematProductionCheckpoint(
        List<EncodedTriple> baseTriples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        string label,
        CheckpointKind kind,
        SharedSubpartDagDelta delta,
        RematProductionInstance[] instances,
        ProductionCheckpointResult productionResult,
        int repeats,
        bool smallRung)
    {
        //The served-store target the branched oracle predicts: base ∪ Compute(base).Derived
        //when consistent, base alone otherwise (the corpus stays consistent, so
        //the overlay is always on, but the branch is honoured). This is one untimed remat,
        //outside the commit timed regions.
        OwlRlResult oracleResult = OwlRlClosure.Compute(baseTriples, terms, oracle);
        HashSet<EncodedTriple> servedTarget = [.. baseTriples];
        if(oracleResult.IsConsistent)
        {
            servedTarget.UnionWith(oracleResult.Derived);
        }

        //The full commit cost, one commit per remat-serving dataset. Each commit opens a
        //session, applies the net delta, and commits — inside CommitAsync the remat delegate
        //recomputes the full closure and rebuilds the served delta, so the timed region is
        //the honest remat-production commit cost.
        double[] commitTimes = new double[repeats];
        for(int i = 0; i < repeats; i++)
        {
            commitTimes[i] = await instances[i].TimeCommitAsync(delta.Added, delta.Retracted, CancellationToken.None).ConfigureAwait(false);
        }

        Array.Sort(commitTimes);
        double commitMin = commitTimes[0];
        double commitMedian = commitTimes[repeats / 2];
        double commitMax = commitTimes[^1];

        //Correctness teeth: the served store equals base ∪ Compute(base).Derived. Every
        //instance is byte-identical, so the small rung checks them all and the large rung
        //caps to instance 0 (documented cap).
        bool servedOk = true;
        int checkedInstances = smallRung ? repeats : 1;
        for(int i = 0; i < checkedInstances; i++)
        {
            HashSet<EncodedTriple> served = [.. instances[i].ServedTriples()];
            if(!served.SetEquals(servedTarget))
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: remat-production instance {i} served store diverges from base ∪ Compute(base).Derived (served {served.Count}, target {servedTarget.Count})");
                servedOk = false;
            }
        }

        double maintainedMedian = productionResult.CommitMedianMilliseconds;
        double speedup = commitMedian > 0.0 ? commitMedian / maintainedMedian : 0.0;
        Console.WriteLine(
            $"[subpart-dag]   {label,-10} REMAT-PRODUCTION commit median={commitMedian,9:F2} ms (min {commitMin:F2}, max {commitMax:F2}) maintained(same-run) median={maintainedMedian:F2} speedup(maintained vs remat-production)=x{speedup:F1} served={(servedOk ? "MATCH" : "MISMATCH")} overlayOn={oracleResult.IsConsistent}");

        return new RematProductionCheckpointResult(label, kind, commitMedian, commitMin, commitMax, servedOk);
    }

    /// <summary>Measures one production checkpoint: the full commit cost over <paramref name="repeats"/> reasoned datasets, the same-run rule-remat baseline, the served-store correctness teeth, and the counter-identity certification against the engine-level lane.</summary>
    /// <param name="baseTriples">The post-op asserted base the remat closes over and the served store must reproduce the closure of.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="label">The checkpoint label.</param>
    /// <param name="kind">Whether this checkpoint follows an append or a retract burst.</param>
    /// <param name="delta">The op's net base delta, applied as one session commit per instance.</param>
    /// <param name="instances">The reasoned datasets, one per repeat, advanced by this checkpoint's commit.</param>
    /// <param name="engineResult">The engine-level maintained lane's result at this checkpoint — the counter-identity comparand.</param>
    /// <param name="repeats">The timed-run count.</param>
    /// <param name="smallRung">Whether to run the served-correctness teeth over every repeat rather than instance 0 only.</param>
    /// <returns>The checkpoint's measured production result.</returns>
    private static async Task<ProductionCheckpointResult> MeasureProductionCheckpoint(
        List<EncodedTriple> baseTriples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        string label,
        CheckpointKind kind,
        SharedSubpartDagDelta delta,
        ProductionInstance[] instances,
        CheckpointResult engineResult,
        int repeats,
        bool smallRung)
    {
        //The same-run rule-remat baseline: the letter reads the production
        //commit against the rule-remat MIN measured in this same pass, so both
        //see the same warmup and machine state. The last run's derived set is the
        //served-store correctness oracle.
        double[] rematTimes = new double[repeats];
        HashSet<EncodedTriple> rematDerived = [];
        bool rematConsistent = true;
        for(int run = 0; run < repeats; run++)
        {
            long start = Stopwatch.GetTimestamp();
            OwlRlResult result = OwlRlClosure.Compute(baseTriples, terms, oracle);
            rematTimes[run] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if(run == repeats - 1)
            {
                rematDerived = [.. result.Derived];
                rematConsistent = result.IsConsistent;
            }
        }

        Array.Sort(rematTimes);
        double rematMin = rematTimes[0];
        double rematMedian = rematTimes[repeats / 2];

        //The served-store target the branched oracle predicts: base ∪ derived
        //when consistent, base alone otherwise (the corpus stays
        //consistent, so the overlay is always on, but the branch is honoured).
        HashSet<EncodedTriple> servedTarget = [.. baseTriples];
        if(rematConsistent)
        {
            servedTarget.UnionWith(rematDerived);
        }

        //The full commit cost, one commit per reasoned dataset. Each commit opens
        //a session, applies the net delta, and commits — the production path the
        //gate letter reads.
        double[] commitTimes = new double[repeats];
        for(int i = 0; i < repeats; i++)
        {
            commitTimes[i] = await instances[i].TimeCommitAsync(delta.Added, delta.Retracted, CancellationToken.None).ConfigureAwait(false);
        }

        Array.Sort(commitTimes);
        double commitMin = commitTimes[0];
        double commitMedian = commitTimes[repeats / 2];
        double commitMax = commitTimes[^1];

        //Correctness teeth: the served store equals the branched oracle. Every
        //instance is byte-identical, so the small rung checks them all and the
        //large rung caps to instance 0 (documented cap).
        bool servedOk = true;
        int checkedInstances = smallRung ? repeats : 1;
        for(int i = 0; i < checkedInstances; i++)
        {
            HashSet<EncodedTriple> served = [.. instances[i].ServedTriples()];
            if(!served.SetEquals(servedTarget))
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: production instance {i} served store diverges from base ∪ remat.Derived (served {served.Count}, target {servedTarget.Count})");
                servedOk = false;
            }
        }

        //Counter identity: instance 0's captured commit carries the maintenance
        //statistics; they must be byte-identical to the engine-level lane's Apply
        //over the identical (base, delta) pair, and every eligible commit must be
        //Incremental (the D-COUNTERS certification the gate letter needs).
        ReasoningMaintainedCommit captured = instances[0].LastCommit;
        bool modeIncremental = captured.Statistics.Mode == ReasoningMaintenanceMode.Incremental;
        if(!modeIncremental)
        {
            Console.WriteLine($"[subpart-dag] MISMATCH {label}: production commit mode {captured.Statistics.Mode} is not Incremental");
        }

        bool countersIdentical = CountersByteIdentical(captured.Statistics, engineResult.MaintainedStatistics);
        if(!countersIdentical)
        {
            Console.WriteLine(
                $"[subpart-dag] MISMATCH {label}: production counters diverge from the engine-level lane (production {DescribeStatistics(captured.Statistics)} vs engine marked={engineResult.MaintainedStatistics.OverdeleteMarked} rounds={engineResult.MaintainedStatistics.DeletionRounds} rederived={engineResult.MaintainedStatistics.DirectlyRederived} restored={engineResult.MaintainedStatistics.RestoredTotal} insert={engineResult.MaintainedStatistics.InsertRounds} refire={engineResult.MaintainedStatistics.ChoiceOwnerReFires} demote={engineResult.MaintainedStatistics.BaseDemotions} promote={engineResult.MaintainedStatistics.BasePromotions})");
        }

        //Cross-instance counter agreement, mirroring the engine-level lane's
        //determinism pin.
        for(int i = 1; i < repeats; i++)
        {
            if(!instances[i].LastCommit.Statistics.Equals(captured.Statistics))
            {
                Console.WriteLine($"[subpart-dag] MISMATCH {label}: production instance {i} counters differ from instance 0");
                countersIdentical = false;
            }
        }

        double speedup = commitMedian > 0.0 ? rematMedian / commitMedian : 0.0;
        double halfRematMin = rematMin / 2.0;
        bool gatePass = commitMax <= halfRematMin;
        Console.WriteLine(
            $"[subpart-dag]   {label,-10} PRODUCTION commit median={commitMedian,9:F2} ms (min {commitMin:F2}, max {commitMax:F2}) remat(same-run) median={rematMedian:F2} min={rematMin:F2} speedup=x{speedup:F1} served={(servedOk ? "MATCH" : "MISMATCH")} counters={(countersIdentical ? "IDENTICAL" : "DIVERGED")} [{DescribeStatistics(captured.Statistics)}] mode={captured.Statistics.Mode} overlayOn={captured.OverlayOn} derived={captured.DerivedCount:N0}");

        return new ProductionCheckpointResult(label, kind, commitMedian, commitMin, commitMax, rematMin, servedOk, modeIncremental, countersIdentical, gatePass);
    }

    /// <summary>
    /// Prints the per-rung PRODUCTION latency-gate block: the correctness/mode/counter flags, then the TWO gate
    /// letters. The BARE-COMPUTE letter reads the maintained-production commit MAX against half the same-run bare
    /// rule-remat (<see cref="OwlRlClosure.Compute"/>) MIN — the pre-existing criterion. The SYMMETRIC letter reads
    /// the maintained-production commit MAX against half the same-run remat-PRODUCTION commit MIN — both paths pay
    /// the identical dataset commit machinery, so only the maintenance strategy is being compared. Each letter
    /// reports per-kind pass tallies and one PASS/FAIL line per checkpoint in the existing latency-gate style; the block
    /// reports figures only and never declares the verdict.
    /// </summary>
    /// <param name="results">The maintained-production lane's per-checkpoint results, in op order.</param>
    /// <param name="rematResults">The remat-production lane's per-checkpoint results, in op order and index-aligned with <paramref name="results"/> — the symmetric letter's denominator.</param>
    private static void ReportProductionLatencyGate(List<ProductionCheckpointResult> results, List<RematProductionCheckpointResult> rematResults)
    {
        int bareAppendPass = 0;
        int bareAppendTotal = 0;
        int bareBurstPass = 0;
        int bareBurstTotal = 0;
        bool servedOk = true;
        bool modesIncremental = true;
        bool countersIdentical = true;

        foreach(ProductionCheckpointResult result in results)
        {
            servedOk = servedOk && result.ServedOk;
            modesIncremental = modesIncremental && result.ModeIncremental;
            countersIdentical = countersIdentical && result.CountersIdentical;

            if(result.Kind == CheckpointKind.Append)
            {
                bareAppendTotal++;
                if(result.GatePass)
                {
                    bareAppendPass++;
                }
            }
            else if(result.Kind == CheckpointKind.Burst)
            {
                bareBurstTotal++;
                if(result.GatePass)
                {
                    bareBurstPass++;
                }
            }
        }

        bool rematServedOk = true;
        foreach(RematProductionCheckpointResult remat in rematResults)
        {
            rematServedOk = rematServedOk && remat.ServedOk;
        }

        Console.WriteLine(
            $"[subpart-dag]   PRODUCTION latency-gate: served {(servedOk ? "OK" : "FAILED")}, remat-served {(rematServedOk ? "OK" : "FAILED")}, modes {(modesIncremental ? "all Incremental" : "NOT all Incremental")}, counters {(countersIdentical ? "byte-identical" : "DIVERGED")}");

        //The bare-compute letter: maintained-production commit MAX vs half the same-run bare
        //rule-remat MIN (the pre-existing criterion, relabelled for clarity beside the symmetric one).
        Console.WriteLine(
            $"[subpart-dag]   bare-compute letter (maintained-production commit vs same-run bare rule-remat): burst {bareBurstPass}/{bareBurstTotal} pass, append {bareAppendPass}/{bareAppendTotal} pass");
        foreach(ProductionCheckpointResult result in results)
        {
            double halfRematMin = result.RematMinMilliseconds / 2.0;
            Console.WriteLine(
                $"[subpart-dag]     bare-compute {(result.GatePass ? "PASS" : "FAIL")} {result.Label}: maintained commit max={result.CommitMaxMilliseconds:F2} ms {(result.GatePass ? "<=" : ">")} 0.5 x bare-remat min={result.RematMinMilliseconds:F2} ms (half={halfRematMin:F2} ms)");
        }

        //The symmetric letter: maintained-production commit MAX vs half the same-run
        //remat-PRODUCTION commit MIN — apples to apples, both through the same commit path.
        int symAppendPass = 0;
        int symAppendTotal = 0;
        int symBurstPass = 0;
        int symBurstTotal = 0;
        int pairs = Math.Min(results.Count, rematResults.Count);
        for(int index = 0; index < pairs; index++)
        {
            bool pass = results[index].CommitMaxMilliseconds <= rematResults[index].CommitMinMilliseconds / 2.0;
            if(results[index].Kind == CheckpointKind.Append)
            {
                symAppendTotal++;
                if(pass)
                {
                    symAppendPass++;
                }
            }
            else if(results[index].Kind == CheckpointKind.Burst)
            {
                symBurstTotal++;
                if(pass)
                {
                    symBurstPass++;
                }
            }
        }

        Console.WriteLine(
            $"[subpart-dag]   symmetric letter (maintained-production vs remat-production): burst {symBurstPass}/{symBurstTotal} pass, append {symAppendPass}/{symAppendTotal} pass");
        for(int index = 0; index < pairs; index++)
        {
            double rematCommitMin = rematResults[index].CommitMinMilliseconds;
            double halfRematCommitMin = rematCommitMin / 2.0;
            bool pass = results[index].CommitMaxMilliseconds <= halfRematCommitMin;
            Console.WriteLine(
                $"[subpart-dag]     symmetric {(pass ? "PASS" : "FAIL")} {results[index].Label}: maintained commit max={results[index].CommitMaxMilliseconds:F2} ms {(pass ? "<=" : ">")} 0.5 x remat-production commit min={rematCommitMin:F2} ms (half={halfRematCommitMin:F2} ms)");
        }
    }

    /// <summary>Warms the production commit pipeline — the session open, the delta apply, the maintenance delegate, and the atomic publish — on a throwaway reasoned dataset so first-commit compilation never lands inside a timed checkpoint.</summary>
    /// <returns>The warmup's completion.</returns>
    private static async Task WarmProductionCommitPipeline()
    {
        TermDictionary warmDictionary = new();
        OwlRlTerms warmTerms = new(warmDictionary);
        SharedSubpartDagCorpus warmCorpus = SharedSubpartDagCorpus.Generate(warmDictionary, warmTerms, WarmupEntities, Seed);
        List<EncodedTriple> warmBase = warmCorpus.Snapshot();
        ProductionInstance warmInstance = await ProductionInstance.CreateAsync(warmDictionary, warmBase, ReasoningConfiguration.Default.Policy, CancellationToken.None).ConfigureAwait(false);
        Random warmRandom = new(Seed);

        long warmupStart = Stopwatch.GetTimestamp();
        while(Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < WarmupBudgetMilliseconds)
        {
            SharedSubpartDagDelta appended = warmCorpus.AppendBatch(warmRandom);
            _ = await warmInstance.TimeCommitAsync(appended.Added, appended.Retracted, CancellationToken.None).ConfigureAwait(false);

            SharedSubpartDagDelta burst = warmCorpus.RetractBurst(warmRandom);
            _ = await warmInstance.TimeCommitAsync(burst.Added, burst.Retracted, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Warms the remat-production commit pipeline — the session open, the delta apply, the remat maintenance delegate (full closure recompute + served-delta setdiff), and the atomic publish — on a throwaway remat-serving dataset so first-commit compilation never lands inside a timed checkpoint.</summary>
    /// <returns>The warmup's completion.</returns>
    private static async Task WarmRematProductionCommitPipeline()
    {
        TermDictionary warmDictionary = new();
        OwlRlTerms warmTerms = new(warmDictionary);
        OwlRlDatatypeOracle warmOracle = OwlRlDatatypeOracles.FromDictionary(warmDictionary);
        SharedSubpartDagCorpus warmCorpus = SharedSubpartDagCorpus.Generate(warmDictionary, warmTerms, WarmupEntities, Seed);
        List<EncodedTriple> warmBase = warmCorpus.Snapshot();
        RematProductionInstance warmInstance = await RematProductionInstance.CreateAsync(warmDictionary, warmTerms, warmOracle, warmBase, CancellationToken.None).ConfigureAwait(false);
        Random warmRandom = new(Seed);

        long warmupStart = Stopwatch.GetTimestamp();
        while(Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < WarmupBudgetMilliseconds)
        {
            SharedSubpartDagDelta appended = warmCorpus.AppendBatch(warmRandom);
            _ = await warmInstance.TimeCommitAsync(appended.Added, appended.Retracted, CancellationToken.None).ConfigureAwait(false);

            SharedSubpartDagDelta burst = warmCorpus.RetractBurst(warmRandom);
            _ = await warmInstance.TimeCommitAsync(burst.Added, burst.Retracted, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Runs the small-rung facade smoke (§7.7): opens a reasoned mutable engine through <see cref="VeritasEngine.OpenMutableAsync(System.Collections.Generic.IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/>, drives a handful of ops as INSERT DATA/DELETE DATA <c>UpdateAsync</c> calls (SPARQL text included), and asserts served correctness per op — proving the full facade path composes.</summary>
    /// <returns>The smoke's completion.</returns>
    private static async Task RunFacadeSmoke()
    {
        const string Ex = "http://example.org/vc-smoke/";
        const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
        const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";
        const string TransitiveProperty = "http://www.w3.org/2002/07/owl#TransitiveProperty";

        static DataTriple Triple(string s, string p, string o) =>
            new(new NamedNode(Utf8Strings.From(s)), new NamedNode(Utf8Strings.From(p)), new NamedNode(Utf8Strings.From(o)));

        List<DataTriple> schemaBase =
        [
            Triple(Ex + "Product", RdfsSubClassOf, Ex + "Artifact"),
            Triple(Ex + "partOf", RdfType, TransitiveProperty),
        ];

        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(schemaBase, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        int mismatches = 0;

        static async Task<bool> Ask(VeritasEngine database, string ask) =>
            await database.AskAsync(Utf8Strings.From(ask), cancellationToken: CancellationToken.None).ConfigureAwait(false);

        static async Task Update(VeritasEngine database, string update) =>
            await database.UpdateAsync(Utf8Strings.From(update), cancellationToken: CancellationToken.None).ConfigureAwait(false);

        //A subclass instance: asserting the membership serves the superclass entailment.
        await Update(engine, $"INSERT DATA {{ <{Ex}widget> <{RdfType}> <{Ex}Product> }}").ConfigureAwait(false);
        mismatches += await Ask(engine, $"ASK {{ <{Ex}widget> <{RdfType}> <{Ex}Artifact> }}").ConfigureAwait(false) ? 0 : Report("subclass entailment served after INSERT");

        //A transitive chain: asserting two hops serves the transitive closure edge.
        await Update(engine, $"INSERT DATA {{ <{Ex}leaf> <{Ex}partOf> <{Ex}sub> . <{Ex}sub> <{Ex}partOf> <{Ex}prod> }}").ConfigureAwait(false);
        mismatches += await Ask(engine, $"ASK {{ <{Ex}leaf> <{Ex}partOf> <{Ex}prod> }}").ConfigureAwait(false) ? 0 : Report("transitive closure edge served after INSERT");

        //Deleting the subclass instance withdraws the superclass entailment (no other support).
        await Update(engine, $"DELETE DATA {{ <{Ex}widget> <{RdfType}> <{Ex}Product> }}").ConfigureAwait(false);
        mismatches += !await Ask(engine, $"ASK {{ <{Ex}widget> <{RdfType}> <{Ex}Artifact> }}").ConfigureAwait(false) ? 0 : Report("superclass entailment withdrawn after DELETE");

        Console.WriteLine($"[subpart-dag]   PRODUCTION facade smoke: {(mismatches == 0 ? "MATCH" : "MISMATCH")} — a reasoned mutable engine driven through real UpdateAsync (SPARQL text) served every op correctly ({3 - mismatches}/3)");
    }

    /// <summary>Prints a facade-smoke mismatch and contributes one to the mismatch tally.</summary>
    /// <param name="what">What the failing op expected.</param>
    /// <returns>One, the mismatch contribution.</returns>
    private static int Report(string what)
    {
        Console.WriteLine($"[subpart-dag] MISMATCH facade smoke: {what}");

        return 1;
    }

    /// <summary>Whether the public per-commit maintenance counters equal the engine-level closure's internal counters, field for field (the D-COUNTERS byte-identity certification).</summary>
    /// <param name="production">The production commit's public statistics.</param>
    /// <param name="engine">The engine-level maintained lane's internal statistics.</param>
    /// <returns><see langword="true"/> when every counter matches.</returns>
    private static bool CountersByteIdentical(in ReasoningMaintenanceStatistics production, in OwlRlMaintenanceStatistics engine)
    {
        return production.OverdeleteMarked == engine.OverdeleteMarked
            && production.DeletionRounds == engine.DeletionRounds
            && production.DirectlyRederived == engine.DirectlyRederived
            && production.RestoredTotal == engine.RestoredTotal
            && production.InsertRounds == engine.InsertRounds
            && production.ChoiceOwnerReFires == engine.ChoiceOwnerReFires
            && production.BaseDemotions == engine.BaseDemotions
            && production.BasePromotions == engine.BasePromotions;
    }

    /// <summary>Formats a production commit's maintenance counters for a mismatch line.</summary>
    /// <param name="statistics">The counters to describe.</param>
    /// <returns>A one-line description.</returns>
    private static string DescribeStatistics(in ReasoningMaintenanceStatistics statistics)
    {
        return FormattableString.Invariant(
            $"marked={statistics.OverdeleteMarked} rounds={statistics.DeletionRounds} rederived={statistics.DirectlyRederived} restored={statistics.RestoredTotal} insert={statistics.InsertRounds} refire={statistics.ChoiceOwnerReFires} demote={statistics.BaseDemotions} promote={statistics.BasePromotions}");
    }

    /// <summary>The median remat and maintained times over the checkpoints of one kind.</summary>
    /// <param name="results">All checkpoint results.</param>
    /// <param name="kind">The checkpoint kind to aggregate.</param>
    /// <returns>The median remat milliseconds, the median maintained milliseconds, and the number of such checkpoints.</returns>
    private static (double RematMedian, double MaintainedMedian, int Count) MedianOf(List<CheckpointResult> results, CheckpointKind kind)
    {
        List<double> rematMedians = [];
        List<double> maintainedMedians = [];
        foreach(CheckpointResult result in results)
        {
            if(result.Kind == kind)
            {
                rematMedians.Add(result.MedianMilliseconds);
                if(result.MaintainedMeasured)
                {
                    maintainedMedians.Add(result.MaintainedMedianMilliseconds);
                }
            }
        }

        if(rematMedians.Count == 0)
        {
            return (0.0, 0.0, 0);
        }

        rematMedians.Sort();
        maintainedMedians.Sort();
        double maintainedMedian = maintainedMedians.Count > 0 ? maintainedMedians[maintainedMedians.Count / 2] : 0.0;

        return (rematMedians[rematMedians.Count / 2], maintainedMedian, rematMedians.Count);
    }

    /// <summary>Whether a checkpoint follows the initial base, an append batch, or a retract burst.</summary>
    private enum CheckpointKind
    {
        /// <summary>The initial corpus, before any op.</summary>
        Initial,

        /// <summary>An append batch of new products, certifications and links.</summary>
        Append,

        /// <summary>A retract burst: an attribute-set rewrite and an entity re-resolution (sameAs bridge).</summary>
        Burst,
    }

    /// <summary>
    /// One reasoned dataset of the production lane: a <see cref="MutableSparqlDataset"/> built through the reasoned
    /// <c>CreateAsync</c> path with its maintenance delegate wired to a real <see cref="ReasoningMaintenance"/> — the
    /// exact composition the Database layer performs, minus the SPARQL-text parsing that sits above the seam. The
    /// wrapping binding captures each commit's <see cref="ReasoningMaintainedCommit"/> (statistics, mode, overlay,
    /// derived count) before mapping it onto the Sparql-layer <see cref="MaintainedCommitDelta"/>, so the lane
    /// certifies the D-COUNTERS byte-identity through the public surface.
    /// </summary>
    private sealed class ProductionInstance
    {
        /// <summary>The reasoned dataset the commits drive.</summary>
        private MutableSparqlDataset Dataset { get; }

        /// <summary>The owned maintenance object the wrapping binding drives.</summary>
        private ReasoningMaintenance Maintenance { get; }

        /// <summary>The most recently maintained commit, captured before mapping onto the served delta.</summary>
        public ReasoningMaintainedCommit LastCommit { get; private set; }

        /// <summary>Constructs the instance over its dataset and maintenance object.</summary>
        /// <param name="dataset">The reasoned dataset.</param>
        /// <param name="maintenance">The owned maintenance object.</param>
        private ProductionInstance(MutableSparqlDataset dataset, ReasoningMaintenance maintenance)
        {
            Dataset = dataset;
            Maintenance = maintenance;
        }

        /// <summary>Builds a reasoned dataset over an initial base exactly as the Database layer does: build the maintenance object (one remat), seed the served store with its initial derived overlay, and register the wrapping maintenance binding.</summary>
        /// <param name="dictionary">The shared term dictionary every store and delta encodes with.</param>
        /// <param name="initialBase">The initial asserted base.</param>
        /// <param name="policy">The reasoning selection policy, matching the Database default.</param>
        /// <param name="cancellationToken">A token that aborts the build.</param>
        /// <returns>The built instance, serving the initial closure.</returns>
        public static async ValueTask<ProductionInstance> CreateAsync(TermDictionary dictionary, List<EncodedTriple> initialBase, ReasoningPolicy policy, CancellationToken cancellationToken)
        {
            ReasoningMaintenance maintenance = await ReasoningMaintenance
                .CreateAsync(initialBase, dictionary, policy, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            MutableSparqlDataset dataset = await MutableSparqlDataset
                .CreateAsync(
                    dictionary,
                    initialBase,
                    [.. maintenance.InitialState.ServedAdditions],
                    initialReasoningState: maintenance.InitialState,
                    namedGraphs: null,
                    journalAppend: null,
                    journalRead: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            ProductionInstance instance = new(dataset, maintenance);
            dataset.RegisterMaintenance(instance.MaintainAsync, instance.OnCommitOutcome);

            return instance;
        }

        /// <summary>Applies the net delta as one session commit and returns the full commit wall-clock — <see cref="MutableSparqlDataset.OpenSessionAsync"/> through <see cref="DatasetEditSession.CommitAsync"/> return; the session dispose is untimed.</summary>
        /// <param name="added">The commit's net base additions.</param>
        /// <param name="removed">The commit's net base removals.</param>
        /// <param name="cancellationToken">A token that aborts the commit.</param>
        /// <returns>The full commit wall-clock in milliseconds.</returns>
        public async ValueTask<double> TimeCommitAsync(IReadOnlyCollection<EncodedTriple> added, IReadOnlyCollection<EncodedTriple> removed, CancellationToken cancellationToken)
        {
            long start = Stopwatch.GetTimestamp();
            DatasetEditSession session = await Dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await session.ApplyDeltaAsync(TermId.None, added, removed, cancellationToken).ConfigureAwait(false);
                await session.CommitAsync(cancellationToken).ConfigureAwait(false);

                return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Reads the served (base ∪ derived) store's triples the last commit published.</summary>
        /// <returns>The served store's triples.</returns>
        public IEnumerable<EncodedTriple> ServedTriples()
        {
            return Dataset.Snapshot().DefaultGraph!.Match(TermId.None, TermId.None, TermId.None);
        }

        /// <summary>The wrapping <see cref="ClosureMaintenanceDelegate"/>: runs the real maintenance, captures the commit for the counter-identity certification, and maps it onto the served delta.</summary>
        /// <param name="baseAdded">The commit's net asserted additions.</param>
        /// <param name="baseRemoved">The commit's net asserted removals.</param>
        /// <param name="tentativeAssertedStore">The session's tentative post-op asserted default-graph store.</param>
        /// <param name="wholesaleReplace">Whether the caller detected a wholesale default-graph replacement.</param>
        /// <param name="cancellationToken">A token that aborts maintenance.</param>
        /// <returns>The served delta, the overlay flag, and the captured commit as the opaque payload.</returns>
        private async ValueTask<MaintainedCommitDelta> MaintainAsync(
            IReadOnlyCollection<EncodedTriple> baseAdded,
            IReadOnlyCollection<EncodedTriple> baseRemoved,
            HypertrieGraphStore tentativeAssertedStore,
            bool wholesaleReplace,
            CancellationToken cancellationToken)
        {
            ReasoningMaintainedCommit commit = await Maintenance
                .MaintainCommit(baseAdded, baseRemoved, tentativeAssertedStore, wholesaleReplace, cancellationToken)
                .ConfigureAwait(false);

            LastCommit = commit;

            return new MaintainedCommitDelta
            {
                ServedAdditions = commit.ServedAdditions,
                ServedRemovals = commit.ServedRemovals,
                OverlayOn = commit.OverlayOn,
                ReasoningState = commit,
            };
        }

        /// <summary>The wrapping <see cref="ClosureMaintenanceOutcomeDelegate"/>: forwards the single per-invocation outcome to the maintenance object.</summary>
        /// <param name="landed">Whether the commit linearised.</param>
        private void OnCommitOutcome(bool landed)
        {
            Maintenance.OnCommitOutcome(landed);
        }
    }

    /// <summary>
    /// The remat-based maintenance strategy the remat-production lane wires in place of a
    /// <see cref="ReasoningMaintenance"/>: on each commit it recomputes the full closure over the post-op asserted
    /// base and rebuilds the served delta as the setdiff between the new served target and the previous served store
    /// it tracks. It is the symmetric counterpart of the maintained path's composed-channel served delta — the same
    /// bookkeeping shape (previous-served ∪ new-target setdiff), differing only in that the new target is recomputed
    /// from scratch rather than carried incrementally. It runs under the dataset's maintenance mutex, so its
    /// per-invocation state is single-threaded, and it stages the new served set until the commit's outcome lands —
    /// the O(|served|) served rebuild is intrinsic to remat-serving and is the honest cost the lane measures.
    /// </summary>
    private sealed class RematMaintenance
    {
        /// <summary>The resolved RL vocabulary the closure recompute closes over.</summary>
        private OwlRlTerms Terms { get; }

        /// <summary>The datatype oracle for the closure recompute.</summary>
        private OwlRlDatatypeOracle Oracle { get; }

        /// <summary>The last-landed served store — base ∪ derived — the served delta's previous-generation term; swapped only when a commit lands.</summary>
        private HashSet<EncodedTriple> PreviousServed { get; set; }

        /// <summary>The served target this commit staged, promoted to <see cref="PreviousServed"/> on landing and discarded otherwise; <see langword="null"/> between commits.</summary>
        private HashSet<EncodedTriple>? PendingServed { get; set; }

        /// <summary>Constructs the remat maintenance over the resolved vocabulary and the initial served store.</summary>
        /// <param name="terms">The resolved RL vocabulary.</param>
        /// <param name="oracle">The datatype oracle.</param>
        /// <param name="initialServed">The initial served store (base ∪ initial derived) — the first commit's previous-generation term.</param>
        public RematMaintenance(OwlRlTerms terms, OwlRlDatatypeOracle oracle, IEnumerable<EncodedTriple> initialServed)
        {
            Terms = terms;
            Oracle = oracle;
            PreviousServed = [.. initialServed];
        }

        /// <summary>
        /// The <see cref="ClosureMaintenanceDelegate"/>: recomputes the full closure over the post-op asserted base
        /// (<see cref="OwlRlClosure.Compute"/>), composes the new served target (base ∪ Derived when consistent),
        /// and returns the served delta as the setdiff against the previous served store. The recompute and the
        /// setdiff both run inside the caller's <see cref="DatasetEditSession.CommitAsync"/>, so their cost lands in
        /// the timed commit region. <paramref name="wholesaleReplace"/> is subsumed — the target is always recomputed
        /// from the tentative asserted store — so no incremental fast path exists to bypass.
        /// </summary>
        /// <param name="baseAdded">The commit's net asserted additions; unused — the target is recomputed from the tentative store.</param>
        /// <param name="baseRemoved">The commit's net asserted removals; unused — the target is recomputed from the tentative store.</param>
        /// <param name="tentativeAssertedStore">The session's tentative post-op asserted default-graph store — the recompute base.</param>
        /// <param name="wholesaleReplace">Whether the caller detected a wholesale replacement; irrelevant to remat serving, which always recomputes.</param>
        /// <param name="cancellationToken">A token that aborts the recompute pre-append.</param>
        /// <returns>The served delta and the overlay flag; the reasoning payload is left <see langword="null"/> (the lane certifies through the served store, not a provenance payload).</returns>
        public ValueTask<MaintainedCommitDelta> MaintainAsync(
            IReadOnlyCollection<EncodedTriple> baseAdded,
            IReadOnlyCollection<EncodedTriple> baseRemoved,
            HypertrieGraphStore tentativeAssertedStore,
            bool wholesaleReplace,
            CancellationToken cancellationToken)
        {
            _ = baseAdded;
            _ = baseRemoved;
            _ = wholesaleReplace;

            List<EncodedTriple> newAsserted = [.. tentativeAssertedStore.Match(TermId.None, TermId.None, TermId.None)];
            OwlRlResult result = OwlRlClosure.Compute(newAsserted, Terms, Oracle, cancellationToken: cancellationToken);

            HashSet<EncodedTriple> newServedTarget = [.. newAsserted];
            bool overlayOn = result.IsConsistent;
            if(overlayOn)
            {
                newServedTarget.UnionWith(result.Derived);
            }

            HashSet<EncodedTriple> added = [.. newServedTarget];
            added.ExceptWith(PreviousServed);
            HashSet<EncodedTriple> removed = [.. PreviousServed];
            removed.ExceptWith(newServedTarget);

            PendingServed = newServedTarget;

            return new ValueTask<MaintainedCommitDelta>(new MaintainedCommitDelta
            {
                ServedAdditions = [.. added],
                ServedRemovals = [.. removed],
                OverlayOn = overlayOn,
                ReasoningState = null,
            });
        }

        /// <summary>The <see cref="ClosureMaintenanceOutcomeDelegate"/>: on landing the staged served target becomes the previous-generation term; on a non-landing commit it is discarded, leaving the previous served store standing.</summary>
        /// <param name="landed">Whether the commit linearised.</param>
        public void OnCommitOutcome(bool landed)
        {
            if(landed && PendingServed is { } pending)
            {
                PreviousServed = pending;
            }

            PendingServed = null;
        }
    }

    /// <summary>
    /// One remat-serving dataset of the remat-production lane: a <see cref="MutableSparqlDataset"/> built through the
    /// same reasoned <c>CreateAsync</c> path as <see cref="ProductionInstance"/> — the same commit machinery, mutex,
    /// served ApplyDelta, journal, and Publish — but with a <see cref="RematMaintenance"/> delegate in place of the
    /// incremental <see cref="ReasoningMaintenance"/>. It is the symmetric baseline the letter reads against.
    /// </summary>
    private sealed class RematProductionInstance
    {
        /// <summary>The remat-serving dataset the commits drive.</summary>
        private MutableSparqlDataset Dataset { get; }

        /// <summary>Constructs the instance over its dataset.</summary>
        /// <param name="dataset">The remat-serving dataset.</param>
        private RematProductionInstance(MutableSparqlDataset dataset)
        {
            Dataset = dataset;
        }

        /// <summary>Builds a remat-serving dataset over an initial base: one remat for the initial served overlay (the construction cost), seed the served store with base ∪ derived, and register the remat maintenance delegate.</summary>
        /// <param name="dictionary">The shared term dictionary every store and delta encodes with.</param>
        /// <param name="terms">The resolved RL vocabulary the remat delegate closes over.</param>
        /// <param name="oracle">The datatype oracle the remat delegate uses.</param>
        /// <param name="initialBase">The initial asserted base.</param>
        /// <param name="cancellationToken">A token that aborts the build.</param>
        /// <returns>The built instance, serving the initial closure.</returns>
        public static async ValueTask<RematProductionInstance> CreateAsync(TermDictionary dictionary, OwlRlTerms terms, OwlRlDatatypeOracle oracle, List<EncodedTriple> initialBase, CancellationToken cancellationToken)
        {
            OwlRlResult initial = OwlRlClosure.Compute(initialBase, terms, oracle, cancellationToken: cancellationToken);
            HashSet<EncodedTriple> initialServed = [.. initialBase];
            if(initial.IsConsistent)
            {
                initialServed.UnionWith(initial.Derived);
            }

            RematMaintenance maintenance = new(terms, oracle, initialServed);

            MutableSparqlDataset dataset = await MutableSparqlDataset
                .CreateAsync(
                    dictionary,
                    initialBase,
                    [.. initialServed],
                    initialReasoningState: null,
                    namedGraphs: null,
                    journalAppend: null,
                    journalRead: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            RematProductionInstance instance = new(dataset);
            dataset.RegisterMaintenance(maintenance.MaintainAsync, maintenance.OnCommitOutcome);

            return instance;
        }

        /// <summary>Applies the net delta as one session commit and returns the full commit wall-clock — <see cref="MutableSparqlDataset.OpenSessionAsync"/> through <see cref="DatasetEditSession.CommitAsync"/> return; the session dispose is untimed.</summary>
        /// <param name="added">The commit's net base additions.</param>
        /// <param name="removed">The commit's net base removals.</param>
        /// <param name="cancellationToken">A token that aborts the commit.</param>
        /// <returns>The full commit wall-clock in milliseconds.</returns>
        public async ValueTask<double> TimeCommitAsync(IReadOnlyCollection<EncodedTriple> added, IReadOnlyCollection<EncodedTriple> removed, CancellationToken cancellationToken)
        {
            long start = Stopwatch.GetTimestamp();
            DatasetEditSession session = await Dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await session.ApplyDeltaAsync(TermId.None, added, removed, cancellationToken).ConfigureAwait(false);
                await session.CommitAsync(cancellationToken).ConfigureAwait(false);

                return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Reads the served (base ∪ derived) store's triples the last commit published.</summary>
        /// <returns>The served store's triples.</returns>
        public IEnumerable<EncodedTriple> ServedTriples()
        {
            return Dataset.Snapshot().DefaultGraph!.Match(TermId.None, TermId.None, TermId.None);
        }
    }

    /// <summary>One remat-production checkpoint's measured commit cost and the served-correctness flag — the symmetric letter's denominator source.</summary>
    /// <param name="Label">The checkpoint label.</param>
    /// <param name="Kind">The checkpoint kind.</param>
    /// <param name="CommitMedianMilliseconds">The median full-commit wall-clock.</param>
    /// <param name="CommitMinMilliseconds">The fastest full-commit run — the symmetric letter's denominator.</param>
    /// <param name="CommitMaxMilliseconds">The slowest full-commit run.</param>
    /// <param name="ServedOk">Whether the served store equalled base ∪ Compute(base).Derived at every checked instance.</param>
    private readonly record struct RematProductionCheckpointResult(
        string Label,
        CheckpointKind Kind,
        double CommitMedianMilliseconds,
        double CommitMinMilliseconds,
        double CommitMaxMilliseconds,
        bool ServedOk);

    /// <summary>One production checkpoint's measured commit cost, its same-run remat baseline, and the correctness/mode/counter/gate flags.</summary>
    /// <param name="Label">The checkpoint label.</param>
    /// <param name="Kind">The checkpoint kind.</param>
    /// <param name="CommitMedianMilliseconds">The median full-commit wall-clock.</param>
    /// <param name="CommitMinMilliseconds">The fastest full-commit run.</param>
    /// <param name="CommitMaxMilliseconds">The slowest full-commit run — the gate letter's numerator.</param>
    /// <param name="RematMinMilliseconds">The fastest same-run rule-remat run — the gate letter's denominator.</param>
    /// <param name="ServedOk">Whether the served store equalled the branched oracle at every checked instance.</param>
    /// <param name="ModeIncremental">Whether the commit took the incremental path.</param>
    /// <param name="CountersIdentical">Whether the commit's counters were byte-identical to the engine-level lane's.</param>
    /// <param name="GatePass">Whether the commit-cost MAX was ≤ half the rule-remat MIN (the spread-free ≥2× letter).</param>
    private readonly record struct ProductionCheckpointResult(
        string Label,
        CheckpointKind Kind,
        double CommitMedianMilliseconds,
        double CommitMinMilliseconds,
        double CommitMaxMilliseconds,
        double RematMinMilliseconds,
        bool ServedOk,
        bool ModeIncremental,
        bool CountersIdentical,
        bool GatePass);

    /// <summary>One checkpoint's measured remat cost, maintained cost, and deterministic proxies.</summary>
    /// <param name="Label">The checkpoint label.</param>
    /// <param name="Kind">The checkpoint kind.</param>
    /// <param name="BaseSize">The base triple count.</param>
    /// <param name="DerivedSize">The derived-set size — the primary deterministic proxy.</param>
    /// <param name="MedianMilliseconds">The median remat wall-clock.</param>
    /// <param name="MinMilliseconds">The fastest remat run.</param>
    /// <param name="MaxMilliseconds">The slowest remat run.</param>
    /// <param name="AllocatedBytes">The bytes one remat allocated.</param>
    /// <param name="Derivations">The total derivation firings, or -1 when not measured.</param>
    /// <param name="MaintainedMeasured">Whether the maintained lane ran — false only at the initial checkpoint.</param>
    /// <param name="MaintainedMedianMilliseconds">The median maintained-Apply wall-clock, or 0 when not measured.</param>
    /// <param name="MaintainedMinMilliseconds">The fastest maintained-Apply run, or 0 when not measured.</param>
    /// <param name="MaintainedMaxMilliseconds">The slowest maintained-Apply run, or 0 when not measured.</param>
    /// <param name="MaintainedAllocatedBytes">The bytes instance 0's Apply allocated, or 0 when not measured.</param>
    /// <param name="MaintainedStatistics">Instance 0's maintenance statistics, or the sentinel when not measured.</param>
    /// <param name="CorrectnessOk">Whether every maintained assertion held at this checkpoint.</param>
    /// <param name="ModeIncremental">Whether the maintained Apply took the incremental path.</param>
    private readonly record struct CheckpointResult(
        string Label,
        CheckpointKind Kind,
        int BaseSize,
        int DerivedSize,
        double MedianMilliseconds,
        double MinMilliseconds,
        double MaxMilliseconds,
        long AllocatedBytes,
        long Derivations,
        bool MaintainedMeasured,
        double MaintainedMedianMilliseconds,
        double MaintainedMinMilliseconds,
        double MaintainedMaxMilliseconds,
        long MaintainedAllocatedBytes,
        OwlRlMaintenanceStatistics MaintainedStatistics,
        bool CorrectnessOk,
        bool ModeIncremental);
}
