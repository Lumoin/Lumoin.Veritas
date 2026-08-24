using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak comparing the Free Join route against the engines it interpolates
/// between. Acyclic rungs (a three-arm star ladder and a two-hop chain ladder)
/// hold the batched pipeline as the bar; cyclic rungs (a triangle ladder and a
/// lollipop — a triangle core with a private tail per corner) hold leapfrog,
/// where the batched pipeline declines. Every rung drains one query through
/// four rendezvous policies — the default routing, leapfrog forced, Free Join
/// with the eager trie build, and Free Join with the lazy build — gating on
/// identical row counts before any number counts, then splits the Free Join
/// cost batch-level into the generalized-hash-trie build and the generic-join
/// drive per build mode, at the join-cover depths and at full depth. The lazy
/// mode's build phase is the column-store load and its forcing lands in the
/// drive, so the honest comparison is the build+drive totals side by side; a
/// retained-values line prints each mode's post-drive footprint beside the
/// milliseconds, so a near-zero lazy build number is never read as near-zero
/// cost. A rung whose shape factorises adds the same split for the factorised
/// route — build against emit, both depths, both build modes — gated on the
/// batch standing for the rung's row count at every combination and on the
/// stored-tuple and group counts matching across depths, so a divergence
/// cannot be read as a timing result; a cyclic rung prints one declined line
/// instead. Times are best-of-three after one untimed warm run (which also
/// builds the columnar view); line-oriented output for hand-collation.
/// </summary>
internal static class FreeJoinSoak
{
    /// <summary>The first star arm's (and the chain's first, and the triangle's) predicate.</summary>
    private const uint P1 = 10;

    /// <summary>The second star arm's (and the chain's second) predicate.</summary>
    private const uint P2 = 20;

    /// <summary>The third star arm's (and the star-chain's extended arm's) predicate.</summary>
    private const uint P3 = 30;

    /// <summary>The lollipop tail (and the star-chain's extension hop) predicate.</summary>
    private const uint P4 = 40;

    /// <summary>Runs the soak ladders; <c>--quick</c> anywhere in the arguments runs the smoke-scale protocol.</summary>
    /// <param name="args">The soak arguments.</param>
    /// <returns>The soak run.</returns>
    public static async Task RunFreeJoinSoakAsync(string[] args)
    {
        bool quick = Array.IndexOf(args, "--quick") >= 0;
        int starTarget = quick ? 50_000 : 500_000;
        int[] chainSubjects = quick ? [10_000] : [50_000, 500_000];
        int[] triangleNodes = quick ? [1_000] : [2_000, 20_000];
        int lollipopNodes = quick ? 1_000 : 2_000;

        Console.WriteLine($"[freejoin] star ladder: 3 arms, per-rung flat rows held near {starTarget:N0}");
        foreach(int fan in (int[])[2, 4, 8])
        {
            int subjects = Math.Max(1, starTarget / Math.Max(1, fan * fan * fan));
            await RunRungAsync($"star fan={fan,2}", StarTriples(subjects, fan), StarQuery, CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine("[freejoin] star-chain hybrid: two star arms plus a third arm extended one hop, at the star ladder's fan-4 scale");
        await RunRungAsync("starchain fan= 4", StarChainTriples(Math.Max(1, starTarget / (4 * 4 * 4 * 4)), 4), StarChainQuery, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine("[freejoin] chain ladder: two hops, one terminal per link");
        foreach(int subjects in chainSubjects)
        {
            await RunRungAsync($"chain subjects={subjects,7:N0}", ChainTriples(subjects), ChainQuery, CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine("[freejoin] triangle ladder: deterministic digraph, out-degree 8");
        foreach(int nodes in triangleNodes)
        {
            await RunRungAsync($"triangle nodes={nodes,6:N0}", TriangleTriples(nodes), TriangleQuery, CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine("[freejoin] lollipop: the triangle core with a private four-leaf tail per corner");
        await RunRungAsync($"lollipop nodes={lollipopNodes,6:N0}", LollipopTriples(lollipopNodes), LollipopQuery, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one rung: the four rendezvous routes over one store with the
    /// row-count gate, then the batch-level Free Join build/drive split per
    /// build mode at the join-cover and full depths over the same fixture,
    /// with the per-mode retained-values footprint, and — where the shape
    /// factorises — the same split for the factorised route's build and emit.
    /// </summary>
    /// <param name="label">The rung label.</param>
    /// <param name="triples">The fixture.</param>
    /// <param name="queryBuilder">Builds the rung's query over a fresh registry.</param>
    /// <param name="cancellationToken">The token the drains observe.</param>
    /// <returns>The rung run.</returns>
    private static async Task RunRungAsync(string label, List<EncodedTriple> triples, Func<VariableRegistry, BasicGraphPattern> queryBuilder, CancellationToken cancellationToken)
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        (double defaultMs, long defaultRows) = await TimeRouteAsync(store, QueryEnginePolicy.Default, query, cancellationToken).ConfigureAwait(false);
        (double leapfrogMs, long leapfrogRows) = await TimeRouteAsync(store, QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false }, query, cancellationToken).ConfigureAwait(false);
        (double freeJoinMs, long freeJoinRows) = await TimeRouteAsync(store, QueryEnginePolicy.Default with { PreferFreeJoin = true }, query, cancellationToken).ConfigureAwait(false);
        (double freeJoinLazyMs, long freeJoinLazyRows) = await TimeRouteAsync(store, QueryEnginePolicy.Default with { PreferFreeJoin = true, FreeJoinTrieBuild = FreeJoinTrieBuild.Lazy }, query, cancellationToken).ConfigureAwait(false);

        string agreement = defaultRows == leapfrogRows && defaultRows == freeJoinRows && defaultRows == freeJoinLazyRows ? "MATCH" : "MISMATCH";
        Console.WriteLine(
            $"[freejoin]   {label} triples={triples.Count,9:N0} rows={defaultRows,9:N0} | default {defaultMs,8:F1} ms  leapfrog {leapfrogMs,8:F1} ms  freejoin {freeJoinMs,8:F1} ms  lazy {freeJoinLazyMs,8:F1} ms  {agreement}");

        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        (double coverBuildMs, double coverDriveMs, long coverRows, long coverRetained) = SplitFreeJoin(index, query, joinCover: true, FreeJoinTrieBuild.Eager);
        (double fullBuildMs, double fullDriveMs, long fullRows, long fullRetained) = SplitFreeJoin(index, query, joinCover: false, FreeJoinTrieBuild.Eager);
        (double lazyCoverBuildMs, double lazyCoverDriveMs, long lazyCoverRows, long lazyCoverRetained) = SplitFreeJoin(index, query, joinCover: true, FreeJoinTrieBuild.Lazy);
        (double lazyFullBuildMs, double lazyFullDriveMs, long lazyFullRows, long lazyFullRetained) = SplitFreeJoin(index, query, joinCover: false, FreeJoinTrieBuild.Lazy);
        bool splitRowsAgree = coverRows == defaultRows && fullRows == defaultRows && lazyCoverRows == defaultRows && lazyFullRows == defaultRows;
        string splitAgreement = splitRowsAgree ? "MATCH" : "MISMATCH";
        Console.WriteLine(
            $"[freejoin]     split eager: cover build {coverBuildMs,7:F1} + drive {coverDriveMs,7:F1} ms | full build {fullBuildMs,7:F1} + drive {fullDriveMs,7:F1} ms  {splitAgreement}");
        Console.WriteLine(
            $"[freejoin]     split lazy : cover build {lazyCoverBuildMs,7:F1} + drive {lazyCoverDriveMs,7:F1} ms | full build {lazyFullBuildMs,7:F1} + drive {lazyFullDriveMs,7:F1} ms");
        Console.WriteLine(
            $"[freejoin]     retained   : cover eager {coverRetained,12:N0} lazy {lazyCoverRetained,12:N0} | full eager {fullRetained,12:N0} lazy {lazyFullRetained,12:N0} values");

        FactorizedSplit? factorizable = SplitFactorizedFreeJoin(index, query, joinCover: true, FreeJoinTrieBuild.Eager);
        if(factorizable is null)
        {
            Console.WriteLine("[freejoin]     factorized : declined");

            return;
        }

        FactorizedSplit coverEager = factorizable.Value;
        FactorizedSplit fullEager = SplitFactorizedFreeJoin(index, query, joinCover: false, FreeJoinTrieBuild.Eager)!.Value;
        FactorizedSplit coverLazy = SplitFactorizedFreeJoin(index, query, joinCover: true, FreeJoinTrieBuild.Lazy)!.Value;
        FactorizedSplit fullLazy = SplitFactorizedFreeJoin(index, query, joinCover: false, FreeJoinTrieBuild.Lazy)!.Value;

        bool factorizedRowsAgree = coverEager.FlatRows == defaultRows && fullEager.FlatRows == defaultRows && coverLazy.FlatRows == defaultRows && fullLazy.FlatRows == defaultRows;
        bool factorizedShapeAgrees = coverEager.Tuples == fullEager.Tuples && coverEager.Groups == fullEager.Groups && coverLazy.Tuples == fullLazy.Tuples && coverLazy.Groups == fullLazy.Groups;
        string factorizedAgreement = factorizedRowsAgree && factorizedShapeAgrees ? "MATCH" : "MISMATCH";
        Console.WriteLine(
            $"[freejoin]     factor eager: cover build {coverEager.BuildMilliseconds,7:F1} + emit {coverEager.EmitMilliseconds,7:F1} ms | full build {fullEager.BuildMilliseconds,7:F1} + emit {fullEager.EmitMilliseconds,7:F1} ms  {factorizedAgreement}");
        Console.WriteLine(
            $"[freejoin]     factor lazy : cover build {coverLazy.BuildMilliseconds,7:F1} + emit {coverLazy.EmitMilliseconds,7:F1} ms | full build {fullLazy.BuildMilliseconds,7:F1} + emit {fullLazy.EmitMilliseconds,7:F1} ms");

        //The retained columns are diagnostic, not evidence: an eager trie sums
        //its packed leaf values only, so a full-depth eager relation reports
        //zero however large its node maps are, and the two depths' eager
        //columns are therefore not comparable with each other.
        Console.WriteLine(
            $"[freejoin]     factor size : tuples {coverEager.Tuples,12:N0} flat {coverEager.FlatRows,12:N0} retained cover eager/lazy {coverEager.RetainedValues,12:N0}/{coverLazy.RetainedValues,12:N0} full eager/lazy {fullEager.RetainedValues,12:N0}/{fullLazy.RetainedValues,12:N0} values");
    }

    /// <summary>Drains one route best-of-three after an untimed warm run that also builds the view.</summary>
    /// <param name="store">The system of record.</param>
    /// <param name="policy">The engine policy the rendezvous routes by.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">The token the drains observe.</param>
    /// <returns>The best elapsed milliseconds and the drained row count, negative on a repetition disagreeing with the warm count.</returns>
    private static async Task<(double Milliseconds, long Rows)> TimeRouteAsync(HypertrieGraphStore store, QueryEnginePolicy policy, BasicGraphPattern query, CancellationToken cancellationToken)
    {
        QueryEngineRendezvous rendezvous = new(store, policy);
        long rows = await DrainAsync(rendezvous, query, cancellationToken).ConfigureAwait(false);

        double best = double.MaxValue;
        for(int repetition = 0; repetition < 3; repetition++)
        {
            long start = Stopwatch.GetTimestamp();
            long drained = await DrainAsync(rendezvous, query, cancellationToken).ConfigureAwait(false);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if(elapsed < best)
            {
                best = elapsed;
            }

            if(drained != rows)
            {
                rows = -1;
            }
        }

        return (best, rows);
    }

    /// <summary>Drains the query through the rendezvous, counting solutions.</summary>
    /// <param name="rendezvous">The rendezvous.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">The token the drain observes.</param>
    /// <returns>The solution count.</returns>
    private static async Task<long> DrainAsync(QueryEngineRendezvous rendezvous, BasicGraphPattern query, CancellationToken cancellationToken)
    {
        long rows = 0;
        await foreach(Solution solution in rendezvous.QueryAsync(query, TimeProvider.System, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            rows++;
        }

        return rows;
    }

    /// <summary>
    /// Times the Free Join pipeline's two phases batch-level over the index:
    /// the generalized-hash-trie build per pattern and the generic-join drive,
    /// with each relation at its join-cover depth or at full depth, in the
    /// given trie build mode. The lazy mode's build phase is the column-store
    /// load and its forcing lands in the drive; the retained-values count is
    /// read after the drive, so it reports the footprint a completed query
    /// held in either mode.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The query.</param>
    /// <param name="joinCover">Whether relations build at their join-cover depths; otherwise full depth.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <returns>The build and drive milliseconds, the drained row count, and the relations' post-drive retained values.</returns>
    private static (double BuildMilliseconds, double DriveMilliseconds, long Rows, long RetainedValues) SplitFreeJoin(ColumnarTripleIndex index, BasicGraphPattern query, bool joinCover, FreeJoinTrieBuild trieBuild)
    {
        IReadOnlyList<Variable> order = ColumnarRotationPlanner.TryPlanGlobalOrder(index.OrderSetMode, query)!;
        Dictionary<Variable, int> orderIndex = new(order.Count);
        for(int k = 0; k < order.Count; k++)
        {
            orderIndex[order[k]] = k;
        }

        HashSet<Variable> joins = FreeJoinPipeline.JoinVariablesOf(index, query);

        long buildStart = Stopwatch.GetTimestamp();
        List<GeneralizedHashTrie> relations = new(query.Patterns.Count);
        foreach(TriplePattern pattern in query.Patterns)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, pattern);
            int[] columns = FreeJoinPipeline.OrderedColumns(scanSchema, orderIndex);
            int depth = joinCover ? FreeJoinPipeline.JoinCoverDepth(scanSchema, columns, joins) : columns.Length;
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, pattern), columns[..depth], columns[depth..], trieBuild));
        }

        double buildMilliseconds = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;

        long driveStart = Stopwatch.GetTimestamp();
        long rows = 0;
        foreach(SolutionBatch batch in FreeJoinExecutor.Execute(relations, order))
        {
            rows += batch.Count;
        }

        double driveMilliseconds = Stopwatch.GetElapsedTime(driveStart).TotalMilliseconds;

        long retainedValues = 0;
        foreach(GeneralizedHashTrie relation in relations)
        {
            retainedValues += relation.RetainedValueCount;
        }

        return (buildMilliseconds, driveMilliseconds, rows, retainedValues);
    }

    /// <summary>One factorised split measurement: the two timed phases, the batch's flat-row, stored-tuple and group counts, and the relations' post-emit retained values.</summary>
    /// <param name="BuildMilliseconds">The relation build time.</param>
    /// <param name="EmitMilliseconds">The factorised emit time.</param>
    /// <param name="FlatRows">The number of flat rows the batch stands for, negative when the executor declined the relations.</param>
    /// <param name="Tuples">The number of value tuples the batch stores, negative when the executor declined the relations.</param>
    /// <param name="Groups">The batch's group count, negative when the executor declined the relations.</param>
    /// <param name="RetainedValues">The relations' post-emit retained values.</param>
    private readonly record struct FactorizedSplit(double BuildMilliseconds, double EmitMilliseconds, long FlatRows, long Tuples, long Groups, long RetainedValues);

    /// <summary>
    /// Times the factorised Free Join route's two phases batch-level over the
    /// index: the generalized-hash-trie build per pattern and the factorised
    /// emit, with each relation at its join-cover depth or at full depth, in
    /// the given trie build mode. The relations are built here rather than
    /// through the pipeline, so full depth stays measurable although the
    /// pipeline offers only the cover. The retained-values count is read after
    /// the emit, and the counts the batch reports are read before the arena
    /// backing them is returned.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The query.</param>
    /// <param name="joinCover">Whether relations build at their join-cover depths; otherwise full depth.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <returns>The split measurement, or <see langword="null"/> when the shape is not a star with chain extensions.</returns>
    private static FactorizedSplit? SplitFactorizedFreeJoin(ColumnarTripleIndex index, BasicGraphPattern query, bool joinCover, FreeJoinTrieBuild trieBuild)
    {
        if(!FreeJoinPipeline.TryPlanFactorizedOrder(index, query, out IReadOnlyList<Variable>? order))
        {
            return null;
        }

        Dictionary<Variable, int> orderIndex = new(order.Count);
        for(int k = 0; k < order.Count; k++)
        {
            orderIndex[order[k]] = k;
        }

        HashSet<Variable> joins = FreeJoinPipeline.JoinVariablesOf(index, query);

        long buildStart = Stopwatch.GetTimestamp();
        List<GeneralizedHashTrie> relations = new(query.Patterns.Count);
        foreach(TriplePattern pattern in query.Patterns)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, pattern);
            int[] columns = FreeJoinPipeline.OrderedColumns(scanSchema, orderIndex);
            int depth = joinCover ? FreeJoinPipeline.JoinCoverDepth(scanSchema, columns, joins) : columns.Length;
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, pattern), columns[..depth], columns[depth..], trieBuild));
        }

        double buildMilliseconds = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;

        using FactorizedArena arena = new();
        long emitStart = Stopwatch.GetTimestamp();
        FactorizedBatch? batch = FreeJoinExecutor.ExecuteFactorized(relations, order, arena);
        double emitMilliseconds = Stopwatch.GetElapsedTime(emitStart).TotalMilliseconds;

        long flatRows = batch is null ? -1 : batch.FlatRowCount;
        long tuples = batch is null ? -1 : batch.FactorizedTupleCount;
        long groups = batch is null ? -1 : batch.Groups.Count;

        long retainedValues = 0;
        foreach(GeneralizedHashTrie relation in relations)
        {
            retainedValues += relation.RetainedValueCount;
        }

        return new FactorizedSplit(buildMilliseconds, emitMilliseconds, flatRows, tuples, groups, retainedValues);
    }

    /// <summary>A three-arm star: each subject fans out on every arm.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fan">The per-arm object count per subject.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarTriples(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint subject = 1_000_000 + s;
            for(uint j = 0; j < fan; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, 2_000_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, 4_000_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P3, 6_000_000 + (s * 100) + j));
            }
        }

        return triples;
    }

    /// <summary>The star query <c>?s P1 ?o1 . ?s P2 ?o2 . ?s P3 ?o3</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o1"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "o3"),
            ],
            registry);
    }

    /// <summary>The star-with-chain hybrid: two star arms per subject and a third arm whose objects each fan out one hop further, the shape that exercises the factorised route's nested and extension paths.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fan">The per-arm object count per subject, and the per-branch object count of the extended arm.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarChainTriples(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint subject = 1_000_000 + s;
            for(uint j = 0; j < fan; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, 2_000_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, 4_000_000 + (s * 100) + j));

                uint branch = 6_000_000 + (s * 100) + j;
                triples.Add(EncodedTriple.FromEncoded(subject, P3, branch));
                for(uint c = 0; c < fan; c++)
                {
                    triples.Add(EncodedTriple.FromEncoded(branch, P4, 8_000_000 + (((s * 100) + j) * 10) + c));
                }
            }
        }

        return triples;
    }

    /// <summary>The star-chain query <c>?s P1 ?o1 . ?s P2 ?o2 . ?s P3 ?b . ?b P4 ?c</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarChainQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o1"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "b"),
                EdgePattern(registry, P4, "b", "c"),
            ],
            registry);
    }

    /// <summary>A two-hop chain: each subject reaches one link, each link one terminal.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> ChainTriples(int subjects)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint link = 2_000_000 + s;
            triples.Add(EncodedTriple.FromEncoded(1_000_000 + s, P1, link));
            triples.Add(EncodedTriple.FromEncoded(link, P2, 4_000_000 + s));
        }

        return triples;
    }

    /// <summary>The chain query <c>?s P1 ?o . ?o P2 ?t</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o"),
                EdgePattern(registry, P2, "o", "t"),
            ],
            registry);
    }

    /// <summary>A deterministic sparse digraph on one predicate: eight arithmetic out-edges per node, self-loops skipped.</summary>
    /// <param name="nodes">The node count.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> TriangleTriples(int nodes)
    {
        List<EncodedTriple> triples = [];
        for(uint i = 0; i < nodes; i++)
        {
            for(uint j = 1; j <= 8; j++)
            {
                uint target = ((i * 31) + (j * 17)) % (uint)nodes;
                if(target != i)
                {
                    triples.Add(EncodedTriple.FromEncoded(1_000_000 + i, P1, 1_000_000 + target));
                }
            }
        }

        return triples;
    }

    /// <summary>The triangle query <c>?x P1 ?y . ?y P1 ?z . ?z P1 ?x</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern TriangleQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "x", "y"),
                EdgePattern(registry, P1, "y", "z"),
                EdgePattern(registry, P1, "z", "x"),
            ],
            registry);
    }

    /// <summary>The triangle digraph plus a private four-leaf tail per node — the cyclic core with an acyclic satellite.</summary>
    /// <param name="nodes">The node count.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> LollipopTriples(int nodes)
    {
        List<EncodedTriple> triples = TriangleTriples(nodes);
        for(uint i = 0; i < nodes; i++)
        {
            for(uint j = 0; j < 4; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(1_000_000 + i, P4, 8_000_000 + (i * 10) + j));
            }
        }

        return triples;
    }

    /// <summary>The lollipop query: the triangle plus the private tail <c>?x P4 ?w</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern LollipopQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "x", "y"),
                EdgePattern(registry, P1, "y", "z"),
                EdgePattern(registry, P1, "z", "x"),
                EdgePattern(registry, P4, "x", "w"),
            ],
            registry);
    }

    /// <summary>The pattern <c>?subject {predicate} ?object</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <param name="predicate">The bound predicate.</param>
    /// <param name="subjectName">The subject variable name.</param>
    /// <param name="objectName">The object variable name.</param>
    /// <returns>The pattern.</returns>
    private static TriplePattern EdgePattern(VariableRegistry registry, uint predicate, string subjectName, string objectName)
    {
        return new TriplePattern(
            PatternPosition.OfVariable(registry.GetOrAdd(subjectName)),
            PatternPosition.Bound(TermId.FromEncoded(predicate)),
            PatternPosition.OfVariable(registry.GetOrAdd(objectName)));
    }
}
