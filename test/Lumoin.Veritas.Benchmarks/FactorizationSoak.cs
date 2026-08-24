using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak quantifying the factorised intermediate against the flat row product
/// on the shape it targets: a two-pattern join <c>?s p1 ?o . ?s p2 ?o2</c>
/// where each subject carries many <c>p1</c> objects AND many <c>p2</c>
/// objects, so the join fans out multiplicatively. The flat join materialises
/// every <c>(o, o2)</c> pair; the factorised join keeps the two sides apart
/// per subject, storing their sum where the flat form stores their product.
/// </summary>
/// <remarks>
/// Both paths share the same scans. Per rung the flattened factorised
/// cardinality is verified equal to the flat row count, then the stored
/// footprint (rows versus tuples), build wall-clock, and allocated bytes are
/// reported for each. The final rung sets one side's fan to one so no key
/// fans out — the honest case where the factorisation cannot compress and
/// pays only its grouping overhead. Line-oriented output for hand-collation.
/// </remarks>
internal static class FactorizationSoak
{
    /// <summary>The <c>?s p1 ?o</c> predicate.</summary>
    private const uint P1 = 10;

    /// <summary>The <c>?s p2 ?o2</c> predicate.</summary>
    private const uint P2 = 20;

    /// <summary>The <c>?s p3 ?o3</c> predicate — the third arm of the star.</summary>
    private const uint P3 = 30;

    /// <summary>Runs the soak ladder.</summary>
    public static void RunFactorizationSoak()
    {
        RunConfiguration(subjects: 500, fanP1: 40, fanP2: 40);
        RunConfiguration(subjects: 1_000, fanP1: 60, fanP2: 60);
        RunConfiguration(subjects: 2_000, fanP1: 1, fanP2: 60);

        //The consume side: a three-arm star where the factorised form is
        //carried across both joins (Join then AddBranch) without ever
        //flattening the cubic intermediate the flat pipeline must materialise.
        //Compression is fan²/3 (stored 3·fan vs flat fan³), so fan=1 is the
        //no-win control where the factorised form is strictly larger, fan=2
        //sits just past break-even, and the win grows fast from there.
        RunStarConfiguration(subjects: 2_000, fan: 1);
        RunStarConfiguration(subjects: 1_000, fan: 2);
        RunStarConfiguration(subjects: 200, fan: 20);
        RunStarConfiguration(subjects: 100, fan: 30);

        //Multi-level: a chain ?a p1 ?x . ?x p2 ?b . ?b p3 ?c where the ?a arm
        //is independent of the (?b,?c) sub-tree given ?x, so nesting the ?b
        //branch keeps the intermediate factorised across the branch-variable
        //join the flat pipeline would have to materialise. Compression is
        //(fanA·S)/(fanA+S) with S=fanB·fanC, so it needs fan-out on BOTH the
        //independent arm AND the sub-tree: all-ones is the no-win control,
        //either side alone barely wins, both together wins big.
        RunChainConfiguration(hubs: 2_000, fanA: 1, fanB: 1, fanC: 1);
        RunChainConfiguration(hubs: 500, fanA: 30, fanB: 1, fanC: 1);
        RunChainConfiguration(hubs: 500, fanA: 1, fanB: 15, fanC: 6);
        RunChainConfiguration(hubs: 400, fanA: 30, fanB: 15, fanC: 6);
        RunChainConfiguration(hubs: 300, fanA: 40, fanB: 20, fanC: 5);
    }

    /// <summary>Builds, verifies, and measures one chain rung: the depth-2 factorised form versus the flat streamed pipeline.</summary>
    /// <param name="hubs">The distinct <c>?x</c> hub count.</param>
    /// <param name="fanA">The <c>?a</c> count per hub.</param>
    /// <param name="fanB">The <c>?b</c> count per hub.</param>
    /// <param name="fanC">The <c>?c</c> count per <c>?b</c>.</param>
    private static void RunChainConfiguration(int hubs, int fanA, int fanB, int fanC)
    {
        List<EncodedTriple> triples = BuildChainFixture(hubs, fanA, fanB, fanC);
        SoakStatistics.ReportGraph(triples, $"chain hubs={hubs:N0} fanA={fanA} fanB={fanB} fanC={fanC}");
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);

        VariableRegistry registry = new();
        TriplePattern r1 = EdgePattern(registry, P1, "a", "x");
        TriplePattern r2 = EdgePattern(registry, P2, "x", "b");
        TriplePattern r3 = EdgePattern(registry, P3, "b", "c");
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, r1);
        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, r2);
        IReadOnlyList<Variable> schema3 = ColumnarBatchScan.ScanSchemaOf(index, r3);
        Variable branchVariable = registry.GetOrAdd("b");

        //The flat oracle stays on the streamed left-deep join (no semijoin, no star).
        ColumnarBatchPlan plan = ColumnarBatchPipeline.TryPlan(index, ChainQuery(), useSemijoinReduction: false, useFactorizedStar: false)!;

        long flatExpected = (long)hubs * fanA * fanB * fanC;
        long tupleExpected = (long)hubs * (fanA + (fanB * fanC));
        Console.WriteLine($"[factorize-chain] hubs={hubs:N0} fanA={fanA} fanB={fanB} fanC={fanC} | flat~{flatExpected:N0} factorized~{tupleExpected:N0}");

        long flatRows = CountPipelineFlat(index, plan);
        using FactorizedArena warmArena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch warm = FactorizeChain(index, r1, schema1, r2, schema2, r3, schema3, branchVariable, warmArena);
        Console.WriteLine($"[factorize-chain]   flatRows={flatRows:N0} factorizedFlat={warm.FlatRowCount:N0} {(flatRows == warm.FlatRowCount ? "MATCH" : "MISMATCH")} storedTuples={warm.FactorizedTupleCount:N0} nested={warm.HasNestedBranches}");
        Console.WriteLine($"[factorize-chain]   compression: x{(double)warm.FlatRowCount / Math.Max(warm.FactorizedTupleCount, 1):F1} (rows/tuples)");

        (double flatMs, double flatMiB) = MeasurePipelineFlat(index, plan);
        (double factorMs, double factorMiB) = MeasureChainFactorized(index, r1, schema1, r2, schema2, r3, schema3, branchVariable);

        Console.WriteLine($"[factorize-chain]   flat:       {flatMs,9:F1} ms  {flatMiB,9:F1} MiB");
        Console.WriteLine($"[factorize-chain]   factorized: {factorMs,9:F1} ms  {factorMiB,9:F1} MiB");
        Console.WriteLine($"[factorize-chain]   speedup: x{flatMs / Math.Max(factorMs, 0.01):F1}  alloc: x{flatMiB / Math.Max(factorMiB, 0.01):F1}");
    }

    /// <summary>Factorises the chain: join the first two arms on <c>?x</c>, then nest the <c>?b</c> branch to attach the <c>?b p3 ?c</c> arm.</summary>
    /// <param name="index">The index.</param>
    /// <param name="r1">The first arm pattern.</param>
    /// <param name="schema1">The first arm's scan schema.</param>
    /// <param name="r2">The second arm pattern.</param>
    /// <param name="schema2">The second arm's scan schema.</param>
    /// <param name="r3">The third arm pattern.</param>
    /// <param name="schema3">The third arm's scan schema.</param>
    /// <param name="branchVariable">The <c>?b</c> variable the third arm joins on.</param>
    /// <param name="arena">The arena the factorised buffers are allocated from.</param>
    /// <returns>The depth-2 factorised chain, valid until <paramref name="arena"/> is disposed.</returns>
    private static FactorizedBatch FactorizeChain(
        ColumnarTripleIndex index,
        TriplePattern r1,
        IReadOnlyList<Variable> schema1,
        TriplePattern r2,
        IReadOnlyList<Variable> schema2,
        TriplePattern r3,
        IReadOnlyList<Variable> schema3,
        Variable branchVariable,
        FactorizedArena arena)
    {
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, r1), schema1, ColumnarBatchScan.Scan(index, r2), schema2, arena);

        return FactorizedBatchJoin.NestBranch(firstTwo, branchVariable, ColumnarBatchScan.Scan(index, r3), schema3, arena)!;
    }

    /// <summary>Times and measures the allocations of building the depth-2 factorised chain.</summary>
    /// <param name="index">The index.</param>
    /// <param name="r1">The first arm pattern.</param>
    /// <param name="schema1">The first arm's scan schema.</param>
    /// <param name="r2">The second arm pattern.</param>
    /// <param name="schema2">The second arm's scan schema.</param>
    /// <param name="r3">The third arm pattern.</param>
    /// <param name="schema3">The third arm's scan schema.</param>
    /// <param name="branchVariable">The <c>?b</c> variable the third arm joins on.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasureChainFactorized(
        ColumnarTripleIndex index,
        TriplePattern r1,
        IReadOnlyList<Variable> schema1,
        TriplePattern r2,
        IReadOnlyList<Variable> schema2,
        TriplePattern r3,
        IReadOnlyList<Variable> schema3,
        Variable branchVariable)
    {
        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        SoakWindow window = SoakWindow.Open();
        FactorizedBatch chain = FactorizeChain(index, r1, schema1, r2, schema2, r3, schema3, branchVariable, arena);
        _ = chain.FactorizedTupleCount;
        SoakSample sample = window.Close();

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>The chain query <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c> over a fresh registry, for the flat pipeline plan.</summary>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery()
    {
        VariableRegistry registry = new();

        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "a", "x"),
                EdgePattern(registry, P2, "x", "b"),
                EdgePattern(registry, P3, "b", "c"),
            ],
            registry);
    }

    /// <summary>Builds the chain fixture: each hub fans in from <paramref name="fanA"/> subjects, fans out to <paramref name="fanB"/> objects, and each of those carries <paramref name="fanC"/> further objects.</summary>
    /// <param name="hubs">The distinct <c>?x</c> hub count.</param>
    /// <param name="fanA">The <c>?a</c> count per hub.</param>
    /// <param name="fanB">The <c>?b</c> count per hub.</param>
    /// <param name="fanC">The <c>?c</c> count per <c>?b</c>.</param>
    /// <returns>The fixture triples.</returns>
    private static List<EncodedTriple> BuildChainFixture(int hubs, int fanA, int fanB, int fanC)
    {
        const uint xBase = 1_000_000;
        const uint aBase = 2_000_000;
        const uint bBase = 3_000_000;
        const uint cBase = 4_000_000;

        List<EncodedTriple> triples = [];
        for(int xi = 0; xi < hubs; xi++)
        {
            uint x = xBase + (uint)xi;
            for(int ai = 0; ai < fanA; ai++)
            {
                triples.Add(EncodedTriple.FromEncoded(aBase + (uint)((xi * 1_000) + ai), P1, x));
            }

            for(int bi = 0; bi < fanB; bi++)
            {
                uint b = bBase + (uint)((xi * 1_000) + bi);
                triples.Add(EncodedTriple.FromEncoded(x, P2, b));

                for(int ci = 0; ci < fanC; ci++)
                {
                    triples.Add(EncodedTriple.FromEncoded(b, P3, cBase + (uint)((((xi * 1_000) + bi) * 100) + ci)));
                }
            }
        }

        return triples;
    }

    /// <summary>Builds, verifies, and measures one rung.</summary>
    /// <param name="subjects">The distinct subject count.</param>
    /// <param name="fanP1">The <c>p1</c> object count per subject (the build fan-out).</param>
    /// <param name="fanP2">The <c>p2</c> object count per subject (the probe fan-out).</param>
    private static void RunConfiguration(int subjects, int fanP1, int fanP2)
    {
        List<EncodedTriple> triples = BuildFixture(subjects, fanP1, fanP2);
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);

        VariableRegistry registry = new();
        TriplePattern p1 = EdgePattern(registry, P1, "s", "o");
        TriplePattern p2 = EdgePattern(registry, P2, "s", "o2");
        IReadOnlyList<Variable> buildSchema = ColumnarBatchScan.ScanSchemaOf(index, p1);
        IReadOnlyList<Variable> probeSchema = ColumnarBatchScan.ScanSchemaOf(index, p2);

        long flatExpected = (long)subjects * fanP1 * fanP2;
        long tupleExpected = (long)subjects * (fanP1 + fanP2);
        Console.WriteLine($"[factorize] subjects={subjects:N0} fanP1={fanP1} fanP2={fanP2} | flat~{flatExpected:N0} factorized~{tupleExpected:N0}");

        //Warm the JIT and confirm flatten reproduces the flat cardinality.
        long flatRows = CountFlat(index, p1, buildSchema, p2, probeSchema);
        using FactorizedArena warmArena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch warm = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), buildSchema, ColumnarBatchScan.Scan(index, p2), probeSchema, warmArena);
        Console.WriteLine($"[factorize]   flatRows={flatRows:N0} factorizedFlat={warm.FlatRowCount:N0} {(flatRows == warm.FlatRowCount ? "MATCH" : "MISMATCH")} storedTuples={warm.FactorizedTupleCount:N0} groups={warm.Groups.Count:N0}");
        Console.WriteLine($"[factorize]   compression: x{(double)warm.FlatRowCount / Math.Max(warm.FactorizedTupleCount, 1):F1} (rows/tuples)");

        (double flatMs, double flatMiB) = MeasureFlat(index, p1, buildSchema, p2, probeSchema);
        (double factorMs, double factorMiB) = MeasureFactorized(index, p1, buildSchema, p2, probeSchema);

        Console.WriteLine($"[factorize]   flat:       {flatMs,9:F1} ms  {flatMiB,9:F1} MiB");
        Console.WriteLine($"[factorize]   factorized: {factorMs,9:F1} ms  {factorMiB,9:F1} MiB");
        Console.WriteLine($"[factorize]   speedup: x{flatMs / Math.Max(factorMs, 0.01):F1}  alloc: x{flatMiB / Math.Max(factorMiB, 0.01):F1}");
    }

    /// <summary>Builds, verifies, and measures one three-arm star rung: the factorised form carried across both joins versus the flat pipeline.</summary>
    /// <param name="subjects">The distinct subject count.</param>
    /// <param name="fan">The per-arm object count per subject; the flat result is cubic in it.</param>
    private static void RunStarConfiguration(int subjects, int fan)
    {
        List<EncodedTriple> triples = BuildStarFixture(subjects, fan);
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);

        VariableRegistry registry = new();
        TriplePattern p1 = EdgePattern(registry, P1, "s", "o");
        TriplePattern p2 = EdgePattern(registry, P2, "s", "o2");
        TriplePattern p3 = EdgePattern(registry, P3, "s", "o3");
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, p1);
        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, p2);
        IReadOnlyList<Variable> schema3 = ColumnarBatchScan.ScanSchemaOf(index, p3);
        ColumnarBatchPlan plan = ColumnarBatchPipeline.TryPlan(index, StarQuery())!;

        long flatExpected = (long)subjects * fan * fan * fan;
        long tupleExpected = (long)subjects * 3 * fan;
        Console.WriteLine($"[factorize-star] subjects={subjects:N0} fan={fan} arms=3 | flat~{flatExpected:N0} factorized~{tupleExpected:N0}");

        long flatRows = CountPipelineFlat(index, plan);
        using FactorizedArena warmArena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch warm = FactorizeStar(index, p1, schema1, p2, schema2, p3, schema3, warmArena);
        Console.WriteLine($"[factorize-star]   flatRows={flatRows:N0} factorizedFlat={warm.FlatRowCount:N0} {(flatRows == warm.FlatRowCount ? "MATCH" : "MISMATCH")} storedTuples={warm.FactorizedTupleCount:N0} branches={warm.BranchColumns.Length}");
        Console.WriteLine($"[factorize-star]   compression: x{(double)warm.FlatRowCount / Math.Max(warm.FactorizedTupleCount, 1):F1} (rows/tuples)");

        (double flatMs, double flatMiB) = MeasurePipelineFlat(index, plan);
        (double factorMs, double factorMiB) = MeasureStarFactorized(index, p1, schema1, p2, schema2, p3, schema3);

        Console.WriteLine($"[factorize-star]   flat:       {flatMs,9:F1} ms  {flatMiB,9:F1} MiB");
        Console.WriteLine($"[factorize-star]   factorized: {factorMs,9:F1} ms  {factorMiB,9:F1} MiB");
        Console.WriteLine($"[factorize-star]   speedup: x{flatMs / Math.Max(factorMs, 0.01):F1}  alloc: x{flatMiB / Math.Max(factorMiB, 0.01):F1}");
    }

    /// <summary>Factorises the three-arm star: two arms join, the third attaches as a branch on the shared key.</summary>
    /// <param name="index">The index.</param>
    /// <param name="p1">The first arm pattern.</param>
    /// <param name="schema1">The first arm's scan schema.</param>
    /// <param name="p2">The second arm pattern.</param>
    /// <param name="schema2">The second arm's scan schema.</param>
    /// <param name="p3">The third arm pattern.</param>
    /// <param name="schema3">The third arm's scan schema.</param>
    /// <param name="arena">The arena the factorised buffers are allocated from.</param>
    /// <returns>The factorised star, valid until <paramref name="arena"/> is disposed.</returns>
    private static FactorizedBatch FactorizeStar(
        ColumnarTripleIndex index,
        TriplePattern p1,
        IReadOnlyList<Variable> schema1,
        TriplePattern p2,
        IReadOnlyList<Variable> schema2,
        TriplePattern p3,
        IReadOnlyList<Variable> schema3,
        FactorizedArena arena)
    {
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), schema1, ColumnarBatchScan.Scan(index, p2), schema2, arena);

        return FactorizedBatchJoin.AddBranch(firstTwo, ColumnarBatchScan.Scan(index, p3), schema3, arena)!;
    }

    /// <summary>Times and measures the allocations of building the factorised star.</summary>
    /// <param name="index">The index.</param>
    /// <param name="p1">The first arm pattern.</param>
    /// <param name="schema1">The first arm's scan schema.</param>
    /// <param name="p2">The second arm pattern.</param>
    /// <param name="schema2">The second arm's scan schema.</param>
    /// <param name="p3">The third arm pattern.</param>
    /// <param name="schema3">The third arm's scan schema.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasureStarFactorized(
        ColumnarTripleIndex index,
        TriplePattern p1,
        IReadOnlyList<Variable> schema1,
        TriplePattern p2,
        IReadOnlyList<Variable> schema2,
        TriplePattern p3,
        IReadOnlyList<Variable> schema3)
    {
        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        SoakWindow window = SoakWindow.Open();
        FactorizedBatch star = FactorizeStar(index, p1, schema1, p2, schema2, p3, schema3, arena);
        _ = star.FactorizedTupleCount;
        SoakSample sample = window.Close();

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>Times and measures the allocations of draining a flat pipeline plan.</summary>
    /// <param name="index">The index.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasurePipelineFlat(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        SoakWindow window = SoakWindow.Open();
        _ = CountPipelineFlat(index, plan);
        SoakSample sample = window.Close();

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>Drains a flat pipeline plan, returning the materialised row count.</summary>
    /// <param name="index">The index.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The flat row count.</returns>
    private static long CountPipelineFlat(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        long rows = 0;
        foreach(SolutionBatch batch in ColumnarBatchPipeline.Run(index, plan, VeritasMemoryPool<uint>.Shared))
        {
            rows += batch.Count;
        }

        return rows;
    }

    /// <summary>The three-arm star query <c>?s p1 ?o . ?s p2 ?o2 . ?s p3 ?o3</c> over a fresh registry, for the flat pipeline plan.</summary>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarQuery()
    {
        VariableRegistry registry = new();

        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "o3"),
            ],
            registry);
    }

    /// <summary>Builds the three-arm star fixture: each subject carries <paramref name="fan"/> distinct objects on each of <c>p1</c>, <c>p2</c>, and <c>p3</c>.</summary>
    /// <param name="subjects">The distinct subject count.</param>
    /// <param name="fan">The per-arm object count per subject.</param>
    /// <returns>The fixture triples.</returns>
    private static List<EncodedTriple> BuildStarFixture(int subjects, int fan)
    {
        const uint subjectBase = 1_000_000;
        const uint object1Base = 2_000_000;
        const uint object2Base = 3_000_000;
        const uint object3Base = 4_000_000;

        List<EncodedTriple> triples = [];
        for(int si = 0; si < subjects; si++)
        {
            uint subject = subjectBase + (uint)si;
            for(int oi = 0; oi < fan; oi++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, object1Base + (uint)((si * 1_000) + oi)));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, object2Base + (uint)((si * 1_000) + oi)));
                triples.Add(EncodedTriple.FromEncoded(subject, P3, object3Base + (uint)((si * 1_000) + oi)));
            }
        }

        return triples;
    }

    /// <summary>Times and measures the allocations of building and draining the flat hash join.</summary>
    /// <param name="index">The index.</param>
    /// <param name="buildPattern">The build pattern.</param>
    /// <param name="buildSchema">The build schema.</param>
    /// <param name="probePattern">The probe pattern.</param>
    /// <param name="probeSchema">The probe schema.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasureFlat(
        ColumnarTripleIndex index,
        TriplePattern buildPattern,
        IReadOnlyList<Variable> buildSchema,
        TriplePattern probePattern,
        IReadOnlyList<Variable> probeSchema)
    {
        SoakWindow window = SoakWindow.Open();
        _ = CountFlat(index, buildPattern, buildSchema, probePattern, probeSchema);
        SoakSample sample = window.Close();

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>Times and measures the allocations of building the factorised join.</summary>
    /// <param name="index">The index.</param>
    /// <param name="buildPattern">The build pattern.</param>
    /// <param name="buildSchema">The build schema.</param>
    /// <param name="probePattern">The probe pattern.</param>
    /// <param name="probeSchema">The probe schema.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasureFactorized(
        ColumnarTripleIndex index,
        TriplePattern buildPattern,
        IReadOnlyList<Variable> buildSchema,
        TriplePattern probePattern,
        IReadOnlyList<Variable> probeSchema)
    {
        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        SoakWindow window = SoakWindow.Open();
        FactorizedBatch batch = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, buildPattern), buildSchema, ColumnarBatchScan.Scan(index, probePattern), probeSchema, arena);
        _ = batch.FactorizedTupleCount;
        SoakSample sample = window.Close();

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>Drains the flat hash join, returning the materialised row count.</summary>
    /// <param name="index">The index.</param>
    /// <param name="buildPattern">The build pattern.</param>
    /// <param name="buildSchema">The build schema.</param>
    /// <param name="probePattern">The probe pattern.</param>
    /// <param name="probeSchema">The probe schema.</param>
    /// <returns>The flat row count.</returns>
    private static long CountFlat(
        ColumnarTripleIndex index,
        TriplePattern buildPattern,
        IReadOnlyList<Variable> buildSchema,
        TriplePattern probePattern,
        IReadOnlyList<Variable> probeSchema)
    {
        long rows = 0;
        foreach(SolutionBatch batch in SolutionBatchJoin.HashJoin(
            ColumnarBatchScan.Scan(index, buildPattern), buildSchema, ColumnarBatchScan.Scan(index, probePattern), probeSchema))
        {
            rows += batch.Count;
        }

        return rows;
    }

    /// <summary>The pattern <c>?subject {predicate} ?object</c> over fresh variables.</summary>
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

    /// <summary>Builds the fan-out fixture: each subject carries <paramref name="fanP1"/> distinct <c>p1</c> objects and <paramref name="fanP2"/> distinct <c>p2</c> objects.</summary>
    /// <param name="subjects">The distinct subject count.</param>
    /// <param name="fanP1">The <c>p1</c> object count per subject.</param>
    /// <param name="fanP2">The <c>p2</c> object count per subject.</param>
    /// <returns>The fixture triples.</returns>
    private static List<EncodedTriple> BuildFixture(int subjects, int fanP1, int fanP2)
    {
        const uint subjectBase = 1_000_000;
        const uint object1Base = 2_000_000;
        const uint object2Base = 3_000_000;

        List<EncodedTriple> triples = [];
        for(int si = 0; si < subjects; si++)
        {
            uint subject = subjectBase + (uint)si;
            for(int oi = 0; oi < fanP1; oi++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, object1Base + (uint)((si * 1_000) + oi)));
            }

            for(int oi = 0; oi < fanP2; oi++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P2, object2Base + (uint)((si * 1_000) + oi)));
            }
        }

        return triples;
    }
}
