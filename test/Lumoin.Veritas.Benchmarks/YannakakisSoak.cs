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
/// Soak quantifying Yannakakis' semijoin reduction against the unreduced
/// left-deep stream on the shape it targets: a chain <c>?a p1 ?x . ?x p2 ?b
/// . ?b p3 ?c</c> whose middle join <c>R1 ⋈ R2</c> is a many-to-many cross
/// per <c>?x</c> but whose final answer is small because only a few <c>?b</c>
/// carry a <c>p3</c> edge. The streaming pipeline materialises the whole
/// intermediate then prunes; the reduced pipeline strips the dangling tuples
/// first, so its intermediate tracks the output.
/// </summary>
/// <remarks>
/// Both paths are the shipped <see cref="ColumnarBatchPipeline"/> — the plan
/// with reduction enabled carries a join tree, the one without does not.
/// Per rung the output counts are verified equal, then wall-clock and
/// allocated bytes are reported for each. The final rung disables the
/// selectivity (every <c>?b</c> carries a <c>p3</c> edge) so the reduction
/// has nothing to strip — the honest overhead case. Line-oriented output for
/// hand-collation.
/// </remarks>
internal static class YannakakisSoak
{
    /// <summary>The <c>?a p1 ?x</c> predicate.</summary>
    private const uint P1 = 10;

    /// <summary>The <c>?x p2 ?b</c> predicate.</summary>
    private const uint P2 = 20;

    /// <summary>The <c>?b p3 ?c</c> predicate.</summary>
    private const uint P3 = 30;

    /// <summary>Runs the soak ladder.</summary>
    public static void RunYannakakisSoak()
    {
        RunConfiguration(distinctX: 200, fanIn: 50, fanOut: 50, selectedX: 3);
        RunConfiguration(distinctX: 400, fanIn: 40, fanOut: 40, selectedX: 5);
        RunConfiguration(distinctX: 200, fanIn: 50, fanOut: 50, selectedX: 200);
    }

    /// <summary>Builds, verifies, and measures one rung.</summary>
    /// <param name="distinctX">The number of distinct <c>?x</c> hubs.</param>
    /// <param name="fanIn">The <c>?a</c> count per hub (the <c>R1</c> fan-in).</param>
    /// <param name="fanOut">The <c>?b</c> count per hub (the <c>R2</c> fan-out).</param>
    /// <param name="selectedX">The number of hubs whose <c>?b</c> values carry a <c>p3</c> edge; below <paramref name="distinctX"/> the join is selective.</param>
    private static void RunConfiguration(int distinctX, int fanIn, int fanOut, int selectedX)
    {
        List<EncodedTriple> triples = BuildFixture(distinctX, fanIn, fanOut, selectedX, out int r1, out int r2, out int r3);
        SoakStatistics.ReportGraph(triples, $"x={distinctX:N0} fanIn={fanIn} fanOut={fanOut}");
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        BasicGraphPattern query = BuildQuery();

        ColumnarBatchPlan? reduced = ColumnarBatchPipeline.TryPlan(index, query, useSemijoinReduction: true);
        ColumnarBatchPlan? streamed = ColumnarBatchPipeline.TryPlan(index, query, useSemijoinReduction: false);

        long intermediate = (long)distinctX * fanIn * fanOut;
        Console.WriteLine($"[yannakakis] x={distinctX:N0} fanIn={fanIn} fanOut={fanOut} selectedX={selectedX} | R1={r1:N0} R2={r2:N0} R3={r3:N0} streamIntermediate~{intermediate:N0}");

        if(reduced is null || streamed is null)
        {
            Console.WriteLine($"[yannakakis]   UNPLANNED (selectedX={selectedX})");

            return;
        }

        Console.WriteLine($"[yannakakis]   tree: reduced={(reduced.JoinTree is null ? "none" : "present")} streamed={(streamed.JoinTree is null ? "none" : "present")} order={reduced.Order.Count}");

        //Warm the JIT and confirm the answers match before timing.
        int reducedRows = Count(index, reduced);
        int streamRows = Count(index, streamed);
        Console.WriteLine($"[yannakakis]   output rows: reduced={reducedRows:N0} stream={streamRows:N0} {(reducedRows == streamRows ? "MATCH" : "MISMATCH")}");

        (double reducedMs, double reducedMiB) = Measure(index, reduced);
        (double streamMs, double streamMiB) = Measure(index, streamed);

        Console.WriteLine($"[yannakakis]   reduced: {reducedMs,9:F1} ms  {reducedMiB,9:F1} MiB");
        Console.WriteLine($"[yannakakis]   stream:  {streamMs,9:F1} ms  {streamMiB,9:F1} MiB");
        Console.WriteLine($"[yannakakis]   speedup: x{streamMs / Math.Max(reducedMs, 0.01):F1}  alloc: x{streamMiB / Math.Max(reducedMiB, 0.01):F1}");
    }

    /// <summary>Times and measures the allocations of one full run of a plan.</summary>
    /// <param name="index">The index.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) Measure(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        SoakWindow window = SoakWindow.Open();
        _ = Count(index, plan);
        SoakSample sample = window.Close();

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>Drains a plan's batch stream, returning the output row count.</summary>
    /// <param name="index">The index.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The output row count.</returns>
    private static int Count(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        int rows = 0;
        foreach(SolutionBatch batch in ColumnarBatchPipeline.Run(index, plan, VeritasMemoryPool<uint>.Shared))
        {
            rows += batch.Count;
        }

        return rows;
    }

    /// <summary>
    /// Builds the chain fixture: each hub <c>?x</c> fans in from
    /// <paramref name="fanIn"/> distinct <c>?a</c> and fans out to
    /// <paramref name="fanOut"/> distinct <c>?b</c>; the first
    /// <paramref name="selectedX"/> hubs' <c>?b</c> values each carry one
    /// <c>?c</c>, so only those reach a full answer.
    /// </summary>
    /// <param name="distinctX">The hub count.</param>
    /// <param name="fanIn">The <c>?a</c> count per hub.</param>
    /// <param name="fanOut">The <c>?b</c> count per hub.</param>
    /// <param name="selectedX">The number of selective hubs.</param>
    /// <param name="r1">Receives the <c>R1</c> row count.</param>
    /// <param name="r2">Receives the <c>R2</c> row count.</param>
    /// <param name="r3">Receives the <c>R3</c> row count.</param>
    /// <returns>The fixture triples.</returns>
    private static List<EncodedTriple> BuildFixture(int distinctX, int fanIn, int fanOut, int selectedX, out int r1, out int r2, out int r3)
    {
        const uint xBase = 1_000_000;
        const uint aBase = 2_000_000;
        const uint bBase = 3_000_000;
        const uint cBase = 4_000_000;

        List<EncodedTriple> triples = [];
        r1 = 0;
        r2 = 0;
        r3 = 0;
        for(int xi = 0; xi < distinctX; xi++)
        {
            uint x = xBase + (uint)xi;
            for(int ai = 0; ai < fanIn; ai++)
            {
                uint a = aBase + (uint)((xi * 1_000) + ai);
                triples.Add(EncodedTriple.FromEncoded(a, P1, x));
                r1++;
            }

            for(int bi = 0; bi < fanOut; bi++)
            {
                uint b = bBase + (uint)((xi * 1_000) + bi);
                triples.Add(EncodedTriple.FromEncoded(x, P2, b));
                r2++;

                if(xi < selectedX)
                {
                    uint c = cBase + (uint)((xi * 1_000) + bi);
                    triples.Add(EncodedTriple.FromEncoded(b, P3, c));
                    r3++;
                }
            }
        }

        return triples;
    }

    /// <summary>Builds the chain query <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c>.</summary>
    /// <returns>The query.</returns>
    private static BasicGraphPattern BuildQuery()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable x = registry.GetOrAdd("x");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(TermId.FromEncoded(P1)), PatternPosition.OfVariable(x)),
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(P2)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(TermId.FromEncoded(P3)), PatternPosition.OfVariable(c)),
            ],
            registry);
    }
}
