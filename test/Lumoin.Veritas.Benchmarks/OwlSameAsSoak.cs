using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak comparing the <c>owl:sameAs</c> treatments of the RL closure:
/// the production rule-based <c>eq-*</c> materialization
/// (<see cref="OwlRlClosure.Compute"/>), the naive reference evaluation
/// (<see cref="OwlRlClosure.ComputeNaive"/>, the per-pair <c>eq-rep</c>
/// scan over the accumulated set) as the controlled comparand, and the
/// union-find canonicalization (<see cref="OwlRlCanonicalClosure"/>: one
/// representative row per fact, cliques in an equivalence store).
/// </summary>
/// <remarks>
/// <para>
/// Runs a ladder of clique workloads — per configuration the canonical
/// variant always, the rule-based closures only where they complete in
/// soak time (the cutoff is itself the finding). Every timed variant runs
/// a fixed repeat count and reports each run plus the median, so a claimed
/// win must clear run-to-run spread. Prints base/derived sizes,
/// wall-clock, and allocation per variant, a rule-vs-naive derived-set
/// differential wherever both run, and a full expansion differential
/// (canonical expansion equals the rule-based materialization) on the
/// smallest configuration. Line-oriented output for hand-collation into a
/// markdown table.
/// </para>
/// </remarks>
internal static class OwlSameAsSoak
{
    /// <summary>The runs per timed variant; the median is the reported figure.</summary>
    private const int TimedRepeats = 5;

    /// <summary>Runs the soak ladder.</summary>
    public static void RunSameAsSoak()
    {
        RunConfiguration(entityCount: 512, cliqueSize: 4, dataPerEntity: 2, runRuleBased: true, runNaiveComparand: true, verifyDifferential: true);
        RunConfiguration(entityCount: 2_048, cliqueSize: 8, dataPerEntity: 2, runRuleBased: true, runNaiveComparand: true, verifyDifferential: false);
        RunConfiguration(entityCount: 16_384, cliqueSize: 32, dataPerEntity: 2, runRuleBased: true, runNaiveComparand: false, verifyDifferential: false);
        RunConfiguration(entityCount: 65_536, cliqueSize: 32, dataPerEntity: 2, runRuleBased: true, runNaiveComparand: false, verifyDifferential: false);
    }

    /// <summary>Generates, measures, and reports one ladder rung.</summary>
    /// <param name="entityCount">The number of clique-member individuals.</param>
    /// <param name="cliqueSize">The members per <c>sameAs</c> clique (chained, not pairwise).</param>
    /// <param name="dataPerEntity">The data triples asserted per individual.</param>
    /// <param name="runRuleBased">Whether the production rule-based closure is feasible at this size.</param>
    /// <param name="runNaiveComparand">Whether the naive reference evaluation is feasible at this size as the controlled comparand.</param>
    /// <param name="verifyDifferential">Whether to expand the canonical result and compare it against the rule-based materialization.</param>
    private static void RunConfiguration(int entityCount, int cliqueSize, int dataPerEntity, bool runRuleBased, bool runNaiveComparand, bool verifyDifferential)
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = Generate(dictionary, terms, entityCount, cliqueSize, dataPerEntity);
        Console.WriteLine($"[sameas-soak] entities={entityCount:N0} clique={cliqueSize} data/entity={dataPerEntity} base={triples.Count:N0}");
        SoakStatistics.ReportGraph(triples, $"sameas entities={entityCount:N0} clique={cliqueSize}");

        (OwlRlCanonicalResult canonical, double canonicalMedian) = RunTimed(
            "union-find",
            () => OwlRlCanonicalClosure.Compute(triples, terms),
            static result => $"canonicalBase={result.CanonicalBase.Count:N0} derived={result.Result.Derived.Count:N0} cliques={result.Equivalence.CliqueCount:N0}");

        if(runRuleBased)
        {
            (OwlRlResult ruleBased, double ruleMedian) = RunTimed(
                "eq-* rules",
                () => OwlRlClosure.Compute(triples, terms),
                static result => $"derived={result.Derived.Count:N0}");
            Console.WriteLine(
                $"[sameas-soak]   ratio: time x{ruleMedian / Math.Max(canonicalMedian, 0.1):F1}, derived x{ruleBased.Derived.Count / (double)Math.Max(canonical.Result.Derived.Count, 1):F1}");

            if(runNaiveComparand)
            {
                (OwlRlResult naive, double naiveMedian) = RunTimed(
                    "naive oracle",
                    () => OwlRlClosure.ComputeNaive(triples, terms),
                    static result => $"derived={result.Derived.Count:N0}");
                HashSet<EncodedTriple> semiNaiveDerived = [.. ruleBased.Derived];
                Console.WriteLine($"[sameas-soak]   naive/semi-naive: time x{naiveMedian / Math.Max(ruleMedian, 0.1):F1}");
                Console.WriteLine($"[sameas-soak]   rule differential: {(semiNaiveDerived.SetEquals(naive.Derived) && ruleBased.IsConsistent == naive.IsConsistent ? "MATCH" : "MISMATCH")} ({semiNaiveDerived.Count:N0} derived)");
            }

            if(verifyDifferential)
            {
                HashSet<EncodedTriple> ruleTotal = [.. triples, .. ruleBased.Derived];
                HashSet<EncodedTriple> expanded = [.. OwlRlCanonicalClosure.ExpandToMaterialization(canonical, terms)];
                Console.WriteLine($"[sameas-soak]   differential: {(ruleTotal.SetEquals(expanded) ? "MATCH" : "MISMATCH")} ({ruleTotal.Count:N0} triples)");
            }
        }
        else
        {
            Console.WriteLine($"[sameas-soak]   eq-* rules: skipped at clique={cliqueSize}");
        }
    }

    /// <summary>
    /// Runs one timed variant <see cref="TimedRepeats"/> times over the
    /// fixed corpus, printing each run's wall-clock and allocation and then
    /// the median with the spread — the controlled-comparand discipline the
    /// acceptance gate reads. Returns the last run's result and the median.
    /// </summary>
    /// <typeparam name="T">The variant's result type.</typeparam>
    /// <param name="label">The printed variant label.</param>
    /// <param name="run">Runs the variant once.</param>
    /// <param name="describe">Renders the result counts for the per-run line.</param>
    /// <returns>The last run's result and the median milliseconds.</returns>
    private static (T Result, double MedianMilliseconds) RunTimed<T>(string label, Func<T> run, Func<T, string> describe)
    {
        double[] times = new double[TimedRepeats];
        T result = default!;
        for(int i = 0; i < TimedRepeats; i++)
        {
            long start = Stopwatch.GetTimestamp();
            long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            result = run();
            long alloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            times[i] = elapsed.TotalMilliseconds;
            Console.WriteLine($"[sameas-soak]   {label} {i + 1}/{TimedRepeats}: {elapsed.TotalMilliseconds,10:F1} ms  alloc {alloc / (1024.0 * 1024.0),8:F1} MB  {describe(result)}");
        }

        Array.Sort(times);
        Console.WriteLine($"[sameas-soak]   {label} median: {times[TimedRepeats / 2],10:F1} ms  (min {times[0]:F1}, max {times[^1]:F1})");

        return (result, times[TimedRepeats / 2]);
    }

    /// <summary>
    /// Generates the clique workload: individuals chained into
    /// <c>sameAs</c> cliques of the given size, each carrying its own data
    /// triples — the shape where the <c>eq-*</c> rules pay their square.
    /// </summary>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="entityCount">The number of individuals.</param>
    /// <param name="cliqueSize">The members per clique.</param>
    /// <param name="dataPerEntity">The data triples per individual.</param>
    /// <returns>The base triples.</returns>
    private static List<EncodedTriple> Generate(
        TermDictionary dictionary,
        OwlRlTerms terms,
        int entityCount,
        int cliqueSize,
        int dataPerEntity)
    {
        List<EncodedTriple> triples = [];
        TermId[] predicates = new TermId[dataPerEntity];
        for(int j = 0; j < dataPerEntity; j++)
        {
            predicates[j] = Mint(dictionary, $"p{j}");
        }

        for(int i = 0; i < entityCount; i++)
        {
            TermId entity = Mint(dictionary, $"e{i}");

            //Chain within the clique: e_i sameAs e_{i+1} unless i closes
            //its group. The chain (not the full pairwise set) is the
            //input; both variants derive the rest.
            if((i + 1) % cliqueSize != 0 && i + 1 < entityCount)
            {
                triples.Add(EncodedTriple.FromEncoded(entity.Encoded, terms.SameAs.Encoded, Mint(dictionary, $"e{i + 1}").Encoded));
            }

            for(int j = 0; j < dataPerEntity; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(entity.Encoded, predicates[j].Encoded, Mint(dictionary, $"v{i}_{j}").Encoded));
            }
        }

        return triples;
    }

    /// <summary>Mints an IRI in the soak namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/soak/" + local)));
    }
}
