using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak comparing the two <c>owl:sameAs</c> treatments of the RL closure:
/// the rule-based <c>eq-*</c> materialization (quadratic <c>sameAs</c>
/// permutations plus per-member triple copies) against the union-find
/// canonicalization (<see cref="OwlRlCanonicalClosure"/>: one
/// representative row per fact, cliques in an equivalence store).
/// </summary>
/// <remarks>
/// <para>
/// Runs a ladder of clique workloads — per configuration the canonical
/// variant always, the rule-based closure only where its quadratic
/// <c>eq-rep</c> scan completes in soak time (the cutoff is itself the
/// finding) — and prints base/derived sizes, wall-clock, and allocation
/// per variant, plus a full differential (expansion equals the rule-based
/// materialization) on the smallest configuration. Line-oriented output
/// for hand-collation into a markdown table.
/// </para>
/// </remarks>
internal static class OwlSameAsSoak
{
    /// <summary>Runs the soak ladder.</summary>
    public static void RunSameAsSoak()
    {
        RunConfiguration(entityCount: 512, cliqueSize: 4, dataPerEntity: 2, runRuleBased: true, verifyDifferential: true);
        RunConfiguration(entityCount: 2_048, cliqueSize: 8, dataPerEntity: 2, runRuleBased: true, verifyDifferential: false);
        RunConfiguration(entityCount: 16_384, cliqueSize: 32, dataPerEntity: 2, runRuleBased: false, verifyDifferential: false);
        RunConfiguration(entityCount: 65_536, cliqueSize: 32, dataPerEntity: 2, runRuleBased: false, verifyDifferential: false);
    }

    /// <summary>Generates, measures, and reports one ladder rung.</summary>
    /// <param name="entityCount">The number of clique-member individuals.</param>
    /// <param name="cliqueSize">The members per <c>sameAs</c> clique (chained, not pairwise).</param>
    /// <param name="dataPerEntity">The data triples asserted per individual.</param>
    /// <param name="runRuleBased">Whether the rule-based closure is feasible at this size.</param>
    /// <param name="verifyDifferential">Whether to expand the canonical result and compare it against the rule-based materialization.</param>
    private static void RunConfiguration(int entityCount, int cliqueSize, int dataPerEntity, bool runRuleBased, bool verifyDifferential)
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = Generate(dictionary, terms, entityCount, cliqueSize, dataPerEntity);
        Console.WriteLine($"[sameas-soak] entities={entityCount:N0} clique={cliqueSize} data/entity={dataPerEntity} base={triples.Count:N0}");

        long canonicalStart = Stopwatch.GetTimestamp();
        long canonicalAllocBefore = GC.GetTotalAllocatedBytes(precise: true);
        OwlRlCanonicalResult canonical = OwlRlCanonicalClosure.Compute(triples, terms);
        long canonicalAlloc = GC.GetTotalAllocatedBytes(precise: true) - canonicalAllocBefore;
        TimeSpan canonicalElapsed = Stopwatch.GetElapsedTime(canonicalStart);
        Console.WriteLine(
            $"[sameas-soak]   union-find: {canonicalElapsed.TotalMilliseconds,10:F1} ms  alloc {canonicalAlloc / (1024.0 * 1024.0),8:F1} MB  canonicalBase={canonical.CanonicalBase.Count:N0} derived={canonical.Result.Derived.Count:N0} cliques={canonical.Equivalence.CliqueCount:N0}");

        if(runRuleBased)
        {
            long ruleStart = Stopwatch.GetTimestamp();
            long ruleAllocBefore = GC.GetTotalAllocatedBytes(precise: true);
            OwlRlResult ruleBased = OwlRlClosure.Compute(triples, terms);
            long ruleAlloc = GC.GetTotalAllocatedBytes(precise: true) - ruleAllocBefore;
            TimeSpan ruleElapsed = Stopwatch.GetElapsedTime(ruleStart);
            Console.WriteLine(
                $"[sameas-soak]   eq-* rules: {ruleElapsed.TotalMilliseconds,10:F1} ms  alloc {ruleAlloc / (1024.0 * 1024.0),8:F1} MB  derived={ruleBased.Derived.Count:N0}");
            Console.WriteLine(
                $"[sameas-soak]   ratio: time x{ruleElapsed.TotalMilliseconds / Math.Max(canonicalElapsed.TotalMilliseconds, 0.1):F1}, derived x{ruleBased.Derived.Count / (double)Math.Max(canonical.Result.Derived.Count, 1):F1}");

            if(verifyDifferential)
            {
                HashSet<EncodedTriple> ruleTotal = [.. triples, .. ruleBased.Derived];
                HashSet<EncodedTriple> expanded = [.. OwlRlCanonicalClosure.ExpandToMaterialization(canonical, terms)];
                Console.WriteLine($"[sameas-soak]   differential: {(ruleTotal.SetEquals(expanded) ? "MATCH" : "MISMATCH")} ({ruleTotal.Count:N0} triples)");
            }
        }
        else
        {
            Console.WriteLine($"[sameas-soak]   eq-* rules: skipped at clique={cliqueSize} (the per-pair eq-rep scan over the accumulated set is quadratic at this size)");
        }
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
        return dictionary.GetOrAdd(new NamedNode(Utf8String.FromString("http://example.org/soak/" + local)));
    }
}
