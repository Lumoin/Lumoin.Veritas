using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

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
        RunStarConfiguration(subjects: 200, fan: 20);
        RunStarConfiguration(subjects: 100, fan: 30);
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
        FactorizedBatch warm = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), buildSchema, ColumnarBatchScan.Scan(index, p2), probeSchema);
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

        long flatRows = CountStarFlat(index, plan);
        FactorizedBatch warm = FactorizeStar(index, p1, schema1, p2, schema2, p3, schema3);
        Console.WriteLine($"[factorize-star]   flatRows={flatRows:N0} factorizedFlat={warm.FlatRowCount:N0} {(flatRows == warm.FlatRowCount ? "MATCH" : "MISMATCH")} storedTuples={warm.FactorizedTupleCount:N0} branches={warm.BranchColumns.Length}");
        Console.WriteLine($"[factorize-star]   compression: x{(double)warm.FlatRowCount / Math.Max(warm.FactorizedTupleCount, 1):F1} (rows/tuples)");

        (double flatMs, double flatMiB) = MeasureStarFlat(index, plan);
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
    /// <returns>The factorised star.</returns>
    private static FactorizedBatch FactorizeStar(
        ColumnarTripleIndex index,
        TriplePattern p1,
        IReadOnlyList<Variable> schema1,
        TriplePattern p2,
        IReadOnlyList<Variable> schema2,
        TriplePattern p3,
        IReadOnlyList<Variable> schema3)
    {
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), schema1, ColumnarBatchScan.Scan(index, p2), schema2);

        return FactorizedBatchJoin.AddBranch(firstTwo, ColumnarBatchScan.Scan(index, p3), schema3)!;
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        FactorizedBatch star = FactorizeStar(index, p1, schema1, p2, schema2, p3, schema3);
        _ = star.FactorizedTupleCount;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return (elapsed.TotalMilliseconds, allocated / (1024.0 * 1024.0));
    }

    /// <summary>Times and measures the allocations of draining the flat star pipeline.</summary>
    /// <param name="index">The index.</param>
    /// <param name="plan">The star plan.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasureStarFlat(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        _ = CountStarFlat(index, plan);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return (elapsed.TotalMilliseconds, allocated / (1024.0 * 1024.0));
    }

    /// <summary>Drains the flat star pipeline, returning the materialised row count.</summary>
    /// <param name="index">The index.</param>
    /// <param name="plan">The star plan.</param>
    /// <returns>The flat row count.</returns>
    private static long CountStarFlat(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        long rows = 0;
        foreach(SolutionBatch batch in ColumnarBatchPipeline.Run(index, plan))
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        _ = CountFlat(index, buildPattern, buildSchema, probePattern, probeSchema);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return (elapsed.TotalMilliseconds, allocated / (1024.0 * 1024.0));
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        FactorizedBatch batch = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, buildPattern), buildSchema, ColumnarBatchScan.Scan(index, probePattern), probeSchema);
        _ = batch.FactorizedTupleCount;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return (elapsed.TotalMilliseconds, allocated / (1024.0 * 1024.0));
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
