using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak comparing the SPARQL solution-layer join's two strategies over the same materialised
/// <see cref="SparqlSolution"/> sequences: the nested-loop compatibility merge (O(n·m), the prior shape) against the
/// shared-variable hash join built on <see cref="SolutionHashJoinIndex"/> (O(n+m), the adopted shape). The probe loop
/// mirrors the engine's exactly, so the index under test is the real shipped component.
/// </summary>
/// <remarks>
/// Runs a ladder of acyclic two-relation joins (left binds <c>?s, ?o</c>; right binds <c>?o, ?c</c>; shared <c>?o</c>),
/// selective so the output stays linear and the nested loop's wasted comparisons dominate. Per rung it verifies both
/// strategies produce identical output counts, then prints wall-clock and the speedup ratio. Line-oriented output for
/// hand-collation into a markdown table.
/// </remarks>
internal static class SolutionJoinSoak
{
    /// <summary>The shared join variable both relations bind.</summary>
    private static SparqlVariable JoinVariable { get; } = new(Utf8Strings.From("o"));

    /// <summary>The hash-join key set (the single shared variable).</summary>
    private static SparqlVariable[] JoinVariables { get; } = [JoinVariable];

    /// <summary>Runs the soak ladder.</summary>
    public static void RunSolutionJoinSoak()
    {
        RunConfiguration(leftRows: 10_000, distinctKeys: 1_000);
        RunConfiguration(leftRows: 100_000, distinctKeys: 10_000);
        RunConfiguration(leftRows: 100_000, distinctKeys: 100_000);
    }

    /// <summary>Generates, measures, and reports one ladder rung.</summary>
    /// <param name="leftRows">The number of left solutions.</param>
    /// <param name="distinctKeys">The number of distinct <c>?o</c> values (also the right-side row count); selectivity is one right row per key.</param>
    private static void RunConfiguration(int leftRows, int distinctKeys)
    {
        List<SparqlSolution> left = GenerateLeft(leftRows, distinctKeys);
        List<SparqlSolution> right = GenerateRight(distinctKeys);
        Console.WriteLine($"[join-soak] left={leftRows:N0} right={distinctKeys:N0} keys={distinctKeys:N0}");

        //Warm the JIT and the data once before timing either strategy.
        int hashCount = HashJoin(left, right);
        int nestedCount = NestedLoopJoin(left, right);
        Console.WriteLine($"[join-soak]   output rows: hash={hashCount:N0} nested={nestedCount:N0} {(hashCount == nestedCount ? "MATCH" : "MISMATCH")}");

        long hashStart = Stopwatch.GetTimestamp();
        HashJoin(left, right);
        TimeSpan hashElapsed = Stopwatch.GetElapsedTime(hashStart);

        long nestedStart = Stopwatch.GetTimestamp();
        NestedLoopJoin(left, right);
        TimeSpan nestedElapsed = Stopwatch.GetElapsedTime(nestedStart);

        Console.WriteLine($"[join-soak]   hash:   {hashElapsed.TotalMilliseconds,10:F1} ms");
        Console.WriteLine($"[join-soak]   nested: {nestedElapsed.TotalMilliseconds,10:F1} ms");
        Console.WriteLine($"[join-soak]   speedup: x{nestedElapsed.TotalMilliseconds / Math.Max(hashElapsed.TotalMilliseconds, 0.01):F1}");
    }

    /// <summary>The shared-variable hash join: builds the smaller side on <see cref="SolutionHashJoinIndex"/> and probes the larger, exactly as the engine does.</summary>
    /// <param name="left">The left solutions.</param>
    /// <param name="right">The right solutions.</param>
    /// <returns>The joined-row count.</returns>
    private static int HashJoin(List<SparqlSolution> left, List<SparqlSolution> right)
    {
        bool buildLeft = left.Count <= right.Count;
        List<SparqlSolution> buildSide = buildLeft ? left : right;
        List<SparqlSolution> probeSide = buildLeft ? right : left;

        SolutionHashJoinIndex index = SolutionHashJoinIndex.Build(buildSide, JoinVariables);
        int count = 0;
        foreach(SparqlSolution probe in probeSide)
        {
            for(int rowId = index.FirstMatch(probe); rowId >= 0; rowId = index.NextMatch(rowId))
            {
                SparqlSolution build = index.RowAt(rowId);
                _ = buildLeft ? Merge(build, probe) : Merge(probe, build);
                count++;
            }
        }

        return count;
    }

    /// <summary>The nested-loop compatibility merge: every pair tested for compatibility, the prior O(n·m) shape.</summary>
    /// <param name="left">The left solutions.</param>
    /// <param name="right">The right solutions.</param>
    /// <returns>The joined-row count.</returns>
    private static int NestedLoopJoin(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        int count = 0;
        foreach(SparqlSolution outer in left)
        {
            foreach(SparqlSolution inner in right)
            {
                if(AreCompatible(outer, inner))
                {
                    _ = Merge(outer, inner);
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Returns whether two solutions agree on every shared bound variable (§18.1).</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns><see langword="true"/> when compatible.</returns>
    private static bool AreCompatible(SparqlSolution left, SparqlSolution right)
    {
        foreach(SparqlBinding binding in left.Bindings)
        {
            if(right.TryGetValue(binding.Variable, out RdfTerm value) && !value.Equals(binding.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Merges two compatible solutions (left bindings plus right's non-shared bindings).</summary>
    /// <param name="left">The left solution.</param>
    /// <param name="right">The right solution.</param>
    /// <returns>The merged solution.</returns>
    private static SparqlSolution Merge(SparqlSolution left, SparqlSolution right)
    {
        List<SparqlBinding> merged = new(left.Bindings.Count + right.Bindings.Count);
        merged.AddRange(left.Bindings);
        foreach(SparqlBinding binding in right.Bindings)
        {
            if(!left.TryGetValue(binding.Variable, out _))
            {
                merged.Add(binding);
            }
        }

        return new SparqlSolution(merged);
    }

    /// <summary>Generates the left relation: each row binds <c>?s</c> to a distinct subject and <c>?o</c> to a key cycled over the distinct-key range.</summary>
    /// <param name="rows">The row count.</param>
    /// <param name="distinctKeys">The number of distinct <c>?o</c> values.</param>
    /// <returns>The left solutions.</returns>
    private static List<SparqlSolution> GenerateLeft(int rows, int distinctKeys)
    {
        SparqlVariable subject = new(Utf8Strings.From("s"));
        RdfTerm[] keys = Keys(distinctKeys);
        List<SparqlSolution> left = new(rows);
        for(int i = 0; i < rows; i++)
        {
            left.Add(new SparqlSolution(
            [
                new SparqlBinding(subject, Iri("s" + i)),
                new SparqlBinding(JoinVariable, keys[i % distinctKeys]),
            ]));
        }

        return left;
    }

    /// <summary>Generates the right relation: one row per distinct key, binding <c>?o</c> to the key and <c>?c</c> to a carried value.</summary>
    /// <param name="distinctKeys">The number of distinct <c>?o</c> values.</param>
    /// <returns>The right solutions.</returns>
    private static List<SparqlSolution> GenerateRight(int distinctKeys)
    {
        SparqlVariable carry = new(Utf8Strings.From("c"));
        RdfTerm[] keys = Keys(distinctKeys);
        List<SparqlSolution> right = new(distinctKeys);
        for(int j = 0; j < distinctKeys; j++)
        {
            right.Add(new SparqlSolution(
            [
                new SparqlBinding(JoinVariable, keys[j]),
                new SparqlBinding(carry, Iri("c" + j)),
            ]));
        }

        return right;
    }

    /// <summary>Builds the shared array of distinct key terms so both relations bind value-equal <c>?o</c> terms.</summary>
    /// <param name="count">The number of keys.</param>
    /// <returns>The key terms.</returns>
    private static RdfTerm[] Keys(int count)
    {
        RdfTerm[] keys = new RdfTerm[count];
        for(int k = 0; k < count; k++)
        {
            keys[k] = Iri("o" + k);
        }

        return keys;
    }

    /// <summary>Mints a named node in the soak namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From("http://example.org/soak/" + local));
    }
}
