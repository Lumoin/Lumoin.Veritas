using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The bounding probe for the index-arity decision: per-graph 3-position
/// store composition (the shipped design) against one merged store
/// standing in for a quad index's graph-as-join-variable best case.
/// </summary>
/// <remarks>
/// <para>
/// Three measurements per rung of the graph-count ladder, total triple
/// count held constant so only the partitioning varies:
/// </para>
/// <list type="bullet">
/// <item><description><b>Build</b>: G per-graph stores versus one merged store over the same triples — the composition's build overhead and the merged build standing in for a shared-trie quad build without its fourth level.</description></item>
/// <item><description><b>Graph-scoped query</b>: a BGP against one named graph — composition's home turf, a single small store.</description></item>
/// <item><description><b>Cross-graph join</b>: <c>?x p ?y</c> in some graph joined to <c>?y q ?z</c> in some other — composition pays a per-graph evaluation sweep plus an external hash join (the shape the SPARQL dataset layer produces), the merged store answers it as one two-pattern WCOJ.</description></item>
/// </list>
/// <para>
/// The merged store is an optimistic stand-in: a real quad index carries
/// the graph column's storage and build cost and returns graph bindings,
/// which the merged store does not. The probe therefore bounds the
/// composition's fan-out penalty from below; the true fourth-level cost
/// belongs to the succinct-layout design spike. Line-oriented output for
/// hand-collation.
/// </para>
/// </remarks>
internal static class GraphFanOutSoak
{
    /// <summary>Triples per probe rung, held constant across graph counts.</summary>
    private const int TotalTriples = 131_072;

    /// <summary>The number of distinct bridge entities the join meets on — fixed across rungs so the join cardinality stays comparable.</summary>
    private const int BridgeCount = 8_192;

    /// <summary>Runs the probe ladder: the cross-graph sweep, then the graph-correlated fixtures.</summary>
    public static async Task RunGraphFanOutSoakAsync()
    {
        //One untimed pass absorbs first-touch JIT and allocator warmup so
        //the first measured rung is comparable to the rest.
        await RunRungAsync(4, report: false).ConfigureAwait(false);

        foreach(int graphCount in (int[])[4, 64, 512, 4096])
        {
            await RunRungAsync(graphCount, report: true).ConfigureAwait(false);
        }

        foreach(int graphCount in (int[])[4, 64, 512, 4096])
        {
            await RunCorrelatedRungAsync(graphCount, selectiveEvery: 1).ConfigureAwait(false);
        }

        //Selective: only every 32nd graph carries q-edges, so composition
        //pays a per-store visit that mostly finds nothing — the shape
        //where a quad level's leapfrog seek would skip non-contributing
        //graphs wholesale.
        foreach(int graphCount in (int[])[64, 512, 4096])
        {
            await RunCorrelatedRungAsync(graphCount, selectiveEvery: 32).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The graph-correlated fixture: <c>GRAPH ?g { ?x p ?y . ?y q ?z }</c>
    /// — the same-graph constraint a merged triple store cannot normally
    /// express. Bridges mint per graph, so the join can only meet inside
    /// one graph; the merged store's two-pattern WCOJ then computes
    /// exactly the same rows and stands in for a quad index's
    /// graph-correlated descent without its fourth-level cost.
    /// Composition evaluates the two-pattern join once per store.
    /// </summary>
    /// <param name="graphCount">The number of named graphs.</param>
    /// <param name="selectiveEvery">Every how-many-th graph carries q-edges; 1 = every graph joins, larger values make most per-store visits fruitless.</param>
    private static async Task RunCorrelatedRungAsync(int graphCount, int selectiveEvery)
    {
        TermDictionary dictionary = new();
        TermId p = Mint(dictionary, "p");
        TermId q = Mint(dictionary, "q");

        int perGraph = TotalTriples / graphCount;
        List<List<EncodedTriple>> graphs = [];
        List<EncodedTriple> merged = [];
        for(int g = 0; g < graphCount; g++)
        {
            List<EncodedTriple> triples = new(perGraph);
            bool contributes = g % selectiveEvery == 0;
            int bridgesInGraph = Math.Max(1, perGraph / 8);
            for(int i = 0; i < perGraph; i++)
            {
                int entity = (g * perGraph) + i;
                TermId bridge = Mint(dictionary, $"g{g}b{(i >> 1) % bridgesInGraph}");
                EncodedTriple triple = (i & 1) == 0
                    ? EncodedTriple.FromEncoded(Mint(dictionary, $"x{entity}").Encoded, p.Encoded, bridge.Encoded)
                    : contributes
                        ? EncodedTriple.FromEncoded(bridge.Encoded, q.Encoded, Mint(dictionary, $"z{entity}").Encoded)
                        : EncodedTriple.FromEncoded(Mint(dictionary, $"x{entity}").Encoded, p.Encoded, bridge.Encoded);
                triples.Add(triple);
                merged.Add(triple);
            }

            graphs.Add(triples);
        }

        List<HypertrieGraphStore> stores = [];
        long composedAllocBefore = GC.GetTotalAllocatedBytes(precise: true);
        long buildStart = Stopwatch.GetTimestamp();
        foreach(List<EncodedTriple> triples in graphs)
        {
            stores.Add(await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false));
        }

        TimeSpan composedBuild = Stopwatch.GetElapsedTime(buildStart);
        long composedAlloc = GC.GetTotalAllocatedBytes(precise: true) - composedAllocBefore;

        long mergedAllocBefore = GC.GetTotalAllocatedBytes(precise: true);
        buildStart = Stopwatch.GetTimestamp();
        HypertrieGraphStore mergedStore = await HypertrieGraphStore.BuildAsync(merged, VeritasHashing.Default).ConfigureAwait(false);
        TimeSpan mergedBuild = Stopwatch.GetElapsedTime(buildStart);
        long mergedAlloc = GC.GetTotalAllocatedBytes(precise: true) - mergedAllocBefore;

        //The same-graph two-pattern join: composition runs it per store,
        //the merged store once over the graph-local bridges.
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");
        BasicGraphPattern join = new(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(p), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(q), PatternPosition.OfVariable(z)),
            ],
            registry);

        long composedStart = Stopwatch.GetTimestamp();
        long composedRows = 0;
        foreach(HypertrieGraphStore store in stores)
        {
            composedRows += await CountAsync(store, join).ConfigureAwait(false);
        }

        TimeSpan composedJoin = Stopwatch.GetElapsedTime(composedStart);

        long mergedStart = Stopwatch.GetTimestamp();
        long mergedRows = await CountAsync(mergedStore, join).ConfigureAwait(false);
        TimeSpan mergedJoin = Stopwatch.GetElapsedTime(mergedStart);

        Console.WriteLine($"[graph-correlated] graphs={graphCount:N0} perGraph={perGraph:N0} contributing=1/{selectiveEvery}");
        Console.WriteLine($"[graph-correlated]   build: composed {composedBuild.TotalMilliseconds,8:F1} ms / {composedAlloc / (1024.0 * 1024.0),7:F1} MB  merged {mergedBuild.TotalMilliseconds,8:F1} ms / {mergedAlloc / (1024.0 * 1024.0),7:F1} MB  alloc/triple {composedAlloc / (double)TotalTriples,6:F0}/{mergedAlloc / (double)TotalTriples,6:F0} B");
        Console.WriteLine($"[graph-correlated]   same-graph join: composed {composedJoin.TotalMilliseconds,8:F1} ms  merged {mergedJoin.TotalMilliseconds,8:F1} ms  ratio x{composedJoin.TotalMilliseconds / Math.Max(mergedJoin.TotalMilliseconds, 0.1):F2}  rows {composedRows:N0}/{mergedRows:N0}");
    }

    /// <summary>Generates, measures, and reports one ladder rung.</summary>
    /// <param name="graphCount">The number of named graphs the constant triple total partitions into.</param>
    /// <param name="report">Whether to print; the warmup pass runs silent.</param>
    private static async Task RunRungAsync(int graphCount, bool report)
    {
        TermDictionary dictionary = new();
        TermId p = Mint(dictionary, "p");
        TermId q = Mint(dictionary, "q");

        //Edge kind alternates by entity, so every graph carries both
        //kinds and the bridge population is identical at every rung: the
        //cross-graph join always meets on the same fixed bridge set.
        int perGraph = TotalTriples / graphCount;
        List<List<EncodedTriple>> graphs = [];
        List<EncodedTriple> merged = [];
        for(int g = 0; g < graphCount; g++)
        {
            List<EncodedTriple> triples = new(perGraph);
            for(int i = 0; i < perGraph; i++)
            {
                int entity = (g * perGraph) + i;
                TermId bridge = Mint(dictionary, $"y{(entity >> 1) % BridgeCount}");
                EncodedTriple triple = (entity & 1) == 0
                    ? EncodedTriple.FromEncoded(Mint(dictionary, $"x{entity}").Encoded, p.Encoded, bridge.Encoded)
                    : EncodedTriple.FromEncoded(bridge.Encoded, q.Encoded, Mint(dictionary, $"z{entity}").Encoded);
                triples.Add(triple);
                merged.Add(triple);
            }

            graphs.Add(triples);
        }

        if(report)
        {
            Console.WriteLine($"[graph-fanout] graphs={graphCount:N0} perGraph={perGraph:N0} total={TotalTriples:N0} bridges={BridgeCount:N0}");
        }

        //Build: G stores versus one.
        long buildStart = Stopwatch.GetTimestamp();
        List<HypertrieGraphStore> stores = [];
        foreach(List<EncodedTriple> triples in graphs)
        {
            stores.Add(await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false));
        }

        TimeSpan composedBuild = Stopwatch.GetElapsedTime(buildStart);

        buildStart = Stopwatch.GetTimestamp();
        HypertrieGraphStore mergedStore = await HypertrieGraphStore.BuildAsync(merged, VeritasHashing.Default).ConfigureAwait(false);
        TimeSpan mergedBuild = Stopwatch.GetElapsedTime(buildStart);
        if(report)
        {
            Console.WriteLine($"[graph-fanout]   build: composed {composedBuild.TotalMilliseconds,8:F1} ms  merged {mergedBuild.TotalMilliseconds,8:F1} ms  ratio x{composedBuild.TotalMilliseconds / Math.Max(mergedBuild.TotalMilliseconds, 0.1):F2}");
        }

        //Graph-scoped query: one store, one pattern.
        VariableRegistry scopedRegistry = new();
        Variable x = scopedRegistry.GetOrAdd("x");
        Variable y = scopedRegistry.GetOrAdd("y");
        BasicGraphPattern scoped = new(
            [new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(p), PatternPosition.OfVariable(y))],
            scopedRegistry);

        long scopedStart = Stopwatch.GetTimestamp();
        int scopedRows = await CountAsync(stores[0], scoped).ConfigureAwait(false);
        TimeSpan scopedElapsed = Stopwatch.GetElapsedTime(scopedStart);
        if(report)
        {
            Console.WriteLine($"[graph-fanout]   graph-scoped: {scopedElapsed.TotalMilliseconds,8:F1} ms  rows={scopedRows:N0}");
        }

        //Cross-graph join. Composition: sweep every store per pattern,
        //hash-join outside the engine — the dataset layer's shape.
        long composedStart = Stopwatch.GetTimestamp();
        Dictionary<TermId, List<TermId>> left = [];
        foreach(HypertrieGraphStore store in stores)
        {
            await foreach(Solution solution in store.Query(scoped, TimeProvider.System).ConfigureAwait(false))
            {
                if(!solution.TryGetValue(y, out TermId yValue) || !solution.TryGetValue(x, out TermId xValue))
                {
                    continue;
                }

                if(!left.TryGetValue(yValue, out List<TermId>? xs))
                {
                    xs = [];
                    left[yValue] = xs;
                }

                xs.Add(xValue);
            }
        }

        VariableRegistry rightRegistry = new();
        Variable y2 = rightRegistry.GetOrAdd("y");
        Variable z = rightRegistry.GetOrAdd("z");
        BasicGraphPattern right = new(
            [new TriplePattern(PatternPosition.OfVariable(y2), PatternPosition.Bound(q), PatternPosition.OfVariable(z))],
            rightRegistry);

        long composedJoinRows = 0;
        foreach(HypertrieGraphStore store in stores)
        {
            await foreach(Solution solution in store.Query(right, TimeProvider.System).ConfigureAwait(false))
            {
                if(solution.TryGetValue(y2, out TermId yValue) && left.TryGetValue(yValue, out List<TermId>? xs))
                {
                    //Enumerate the join rows rather than adding the count,
                    //so both sides pay per emitted row.
                    foreach(TermId _ in xs)
                    {
                        composedJoinRows++;
                    }
                }
            }
        }

        TimeSpan composedJoin = Stopwatch.GetElapsedTime(composedStart);

        //Merged: the same join as one two-pattern WCOJ.
        VariableRegistry joinRegistry = new();
        Variable jx = joinRegistry.GetOrAdd("x");
        Variable jy = joinRegistry.GetOrAdd("y");
        Variable jz = joinRegistry.GetOrAdd("z");
        BasicGraphPattern join = new(
            [
                new TriplePattern(PatternPosition.OfVariable(jx), PatternPosition.Bound(p), PatternPosition.OfVariable(jy)),
                new TriplePattern(PatternPosition.OfVariable(jy), PatternPosition.Bound(q), PatternPosition.OfVariable(jz)),
            ],
            joinRegistry);

        long mergedStart = Stopwatch.GetTimestamp();
        long mergedJoinRows = await CountAsync(mergedStore, join).ConfigureAwait(false);
        TimeSpan mergedJoin = Stopwatch.GetElapsedTime(mergedStart);

        if(report)
        {
            Console.WriteLine(
                $"[graph-fanout]   cross-graph join: composed {composedJoin.TotalMilliseconds,8:F1} ms  merged {mergedJoin.TotalMilliseconds,8:F1} ms  ratio x{composedJoin.TotalMilliseconds / Math.Max(mergedJoin.TotalMilliseconds, 0.1):F2}  rows {composedJoinRows:N0}/{mergedJoinRows:N0}");
        }
    }

    /// <summary>Counts the query's solutions.</summary>
    /// <param name="store">The store to query.</param>
    /// <param name="query">The pattern.</param>
    /// <returns>The solution count.</returns>
    private static async Task<int> CountAsync(HypertrieGraphStore store, BasicGraphPattern query)
    {
        int count = 0;
        await foreach(Solution _ in store.Query(query, TimeProvider.System).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    /// <summary>Mints an IRI in the soak namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8String.FromString("http://example.org/fanout/" + local)));
    }
}
