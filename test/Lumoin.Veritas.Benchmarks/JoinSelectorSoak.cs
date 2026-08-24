using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak characterising the calibrated join-strategy selector: fan-out ladders
/// over the star and chain shapes, timing the streamed plan, the forced
/// factorised plan, and the calibrated rule's chosen plan per rung, with the
/// rule's estimated compression and its decision printed beside them.
/// The ladder locates the TIME crossover — the rung where factorisation's
/// per-key grouping overhead is repaid — which the rule's engagement
/// thresholds carry; the calibrated column should track the better of the
/// other two on every rung.
/// Line-oriented output for hand-collation; per-rung totals keep flat result
/// sizes comparable across the ladder.
/// </summary>
internal static class JoinSelectorSoak
{
    /// <summary>The first star arm's (and chain's first) predicate.</summary>
    private const uint P1 = 10;

    /// <summary>The second star arm's (and chain's middle) predicate.</summary>
    private const uint P2 = 20;

    /// <summary>The third star arm's (and chain's leaf) predicate.</summary>
    private const uint P3 = 30;

    /// <summary>Runs the soak ladders.</summary>
    public static void RunJoinSelectorSoak()
    {
        Console.WriteLine($"[selector] star ladder: 3 arms, per-rung flat rows held near {1_500_000:N0}");
        foreach(int fan in (int[])[1, 2, 3, 4, 6, 8, 12, 20])
        {
            int subjects = Math.Max(1, 1_500_000 / Math.Max(1, fan * fan * fan));
            RunStarRung(subjects, fan);
        }

        Console.WriteLine($"[selector] chain ladder: fanA/fanB/fanC, per-rung flat rows held near {1_000_000:N0}");
        foreach((int fanA, int fanB, int fanC) in ((int, int, int)[])[(1, 1, 1), (2, 2, 2), (4, 4, 2), (6, 6, 3), (8, 8, 4), (15, 10, 5), (30, 15, 6)])
        {
            int hubs = Math.Max(1, 1_000_000 / Math.Max(1, fanA * fanB * fanC));
            RunChainRung(hubs, fanA, fanB, fanC);
        }
    }

    /// <summary>Times one star rung: streamed, forced-factorised, and adaptive plans over the same index, with the selector's decision.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fan">The per-arm object count per subject.</param>
    private static void RunStarRung(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint subject = 1_000_000 + s;
            for(uint j = 0; j < fan; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, 2_000_000 + (s * 1_000) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, 4_000_000 + (s * 1_000) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P3, 6_000_000 + (s * 1_000) + j));
            }
        }

        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        VariableRegistry registry = new();
        BasicGraphPattern query = new(
            [
                EdgePattern(registry, P1, "s", "o1"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "o3"),
            ],
            registry);

        double compression = fan * fan / 3.0;
        FactorizationEngagement calibrated = CalibratedEngagement(index, query);

        (double streamMs, long streamRows) = TimePlan(index, query, useFactorizedStar: false, useFactorizedChain: false);
        (double forcedMs, long forcedRows) = TimePlan(index, query, useFactorizedStar: true, useFactorizedChain: false);
        (double adaptiveMs, long adaptiveRows) = TimePlan(index, query, calibrated == FactorizationEngagement.Star, calibrated == FactorizationEngagement.Chain);

        string agreement = streamRows == forcedRows && streamRows == adaptiveRows ? "MATCH" : "MISMATCH";
        Console.WriteLine(
            $"[selector]   star fan={fan,2} subjects={subjects,7:N0} flat={streamRows,9:N0} comp~{compression,6:F1} | stream {streamMs,8:F1} ms  forced {forcedMs,8:F1} ms  adaptive {adaptiveMs,8:F1} ms  engaged={calibrated == FactorizationEngagement.Star} {agreement}");

        //The consumer that keeps the compressed form: counting through the
        //factorised build versus draining the flat stream.
        long countStart = Stopwatch.GetTimestamp();
        long? counted = ColumnarBatchPipeline.TryCount(index, query, VeritasMemoryPool<uint>.Shared);
        double countMs = Stopwatch.GetElapsedTime(countStart).TotalMilliseconds;
        string countAgreement = counted == streamRows ? "MATCH" : "MISMATCH";
        Console.WriteLine(
            $"[selector]     count: factorized {countMs,8:F1} ms vs drain {streamMs,8:F1} ms  (x{streamMs / Math.Max(countMs, 0.01):F1})  {countAgreement}");
    }

    /// <summary>Times one chain rung: streamed, forced-factorised, and adaptive plans over the same index, with the selector's decision.</summary>
    /// <param name="hubs">The distinct hub count.</param>
    /// <param name="fanA">The independent-arm count per hub.</param>
    /// <param name="fanB">The branch count per hub.</param>
    /// <param name="fanC">The leaf count per branch.</param>
    private static void RunChainRung(int hubs, int fanA, int fanB, int fanC)
    {
        List<EncodedTriple> triples = [];
        for(uint h = 0; h < hubs; h++)
        {
            uint hub = 1_000_000 + h;
            for(uint a = 0; a < fanA; a++)
            {
                triples.Add(EncodedTriple.FromEncoded(2_000_000 + (h * 1_000) + a, P1, hub));
            }

            for(uint b = 0; b < fanB; b++)
            {
                uint branch = 4_000_000 + (h * 1_000) + b;
                triples.Add(EncodedTriple.FromEncoded(hub, P2, branch));
                for(uint c = 0; c < fanC; c++)
                {
                    triples.Add(EncodedTriple.FromEncoded(branch, P3, 6_000_000 + (((h * 1_000) + b) * 100) + c));
                }
            }
        }

        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        VariableRegistry registry = new();
        BasicGraphPattern query = new(
            [
                EdgePattern(registry, P1, "a", "x"),
                EdgePattern(registry, P2, "x", "b"),
                EdgePattern(registry, P3, "b", "c"),
            ],
            registry);

        double subTree = (double)fanB * fanC;
        double compression = fanA * subTree / (fanA + subTree);
        FactorizationEngagement calibrated = CalibratedEngagement(index, query);

        (double streamMs, long streamRows) = TimePlan(index, query, useFactorizedStar: false, useFactorizedChain: false);
        (double forcedMs, long forcedRows) = TimePlan(index, query, useFactorizedStar: false, useFactorizedChain: true);
        (double adaptiveMs, long adaptiveRows) = TimePlan(index, query, calibrated == FactorizationEngagement.Star, calibrated == FactorizationEngagement.Chain);

        string agreement = streamRows == forcedRows && streamRows == adaptiveRows ? "MATCH" : "MISMATCH";
        Console.WriteLine(
            $"[selector]   chain {fanA,2}/{fanB,2}/{fanC,2} hubs={hubs,7:N0} flat={streamRows,9:N0} comp~{compression,6:F1} | stream {streamMs,8:F1} ms  forced {forcedMs,8:F1} ms  adaptive {adaptiveMs,8:F1} ms  engaged={calibrated == FactorizationEngagement.Chain} {agreement}");
    }

    /// <summary>The factorisation the calibrated rule engages for the query on the index.</summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The engagement the rule states, or <see cref="FactorizationEngagement.Unspecified"/> when the statistics justify none.</returns>
    private static FactorizationEngagement CalibratedEngagement(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        JoinSelectionContext context = new(query, index, JoinShapeAnalysis.Describe(index, query, QueryEnginePolicy.Default), default);

        return JoinStrategySelectors.Calibrated(in context, CancellationToken.None).Factorization;
    }

    /// <summary>Plans with the given engagements and drains the result, returning wall-clock and the flat row count.</summary>
    /// <param name="index">The index.</param>
    /// <param name="query">The query.</param>
    /// <param name="useFactorizedStar">Whether the star route is engaged.</param>
    /// <param name="useFactorizedChain">Whether the chain route is engaged.</param>
    /// <returns>The elapsed milliseconds and the drained row count.</returns>
    private static (double Milliseconds, long Rows) TimePlan(ColumnarTripleIndex index, BasicGraphPattern query, bool useFactorizedStar, bool useFactorizedChain)
    {
        ColumnarBatchPlan plan = ColumnarBatchPipeline.TryPlan(index, query, useSemijoinReduction: true, useFactorizedStar, useFactorizedChain)!;

        long start = Stopwatch.GetTimestamp();
        long rows = 0;
        foreach(SolutionBatch batch in ColumnarBatchPipeline.Run(index, plan, VeritasMemoryPool<uint>.Shared))
        {
            rows += batch.Count;
        }

        return (Stopwatch.GetElapsedTime(start).TotalMilliseconds, rows);
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
