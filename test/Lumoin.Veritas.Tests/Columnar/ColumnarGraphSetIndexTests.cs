using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The columnar shared arena's contract: a graph's view over the
/// shared concatenated columns answers exactly what a standalone
/// per-graph index answers — walks, membership, and joins — and a
/// view's descent never leaves its graph, even when adjacent
/// graphs share every level value.
/// </summary>
[TestClass]
internal sealed class ColumnarGraphSetIndexTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    /// <summary>Builds a graph map with varied shapes: dense, tiny, and one empty-ish graph, with deliberately overlapping term ids across graphs.</summary>
    /// <param name="graphCount">The number of graphs.</param>
    /// <returns>The graphs keyed by graph id.</returns>
    private static Dictionary<TermId, IEnumerable<EncodedTriple>> BuildGraphs(int graphCount)
    {
        Dictionary<TermId, IEnumerable<EncodedTriple>> graphs = [];
        ulong state = 11;

        for(int g = 0; g < graphCount; g++)
        {
            List<EncodedTriple> triples = [];
            state = Mix(state);
            int size = 1 + (int)(state % 60);

            for(int i = 0; i < size; i++)
            {
                state = Mix(state);

                //Term ids deliberately overlap across graphs so the
                //shared columns interleave identical values at run
                //boundaries.
                triples.Add(EncodedTriple.FromEncoded(
                    100 + (uint)(state % 50),
                    200 + (uint)((state >> 8) % 5),
                    300 + (uint)((state >> 16) % 40)));
            }

            graphs[TermId.FromEncoded(10_000 + (uint)g)] = triples;
        }

        return graphs;
    }

    /// <summary>Order-insensitive solution fingerprints from an evaluator.</summary>
    /// <param name="evaluator">The evaluator.</param>
    /// <returns>The sorted fingerprints.</returns>
    private async Task<List<string>> DrainAsync(ColumnarBasicGraphPatternEvaluator evaluator)
    {
        List<string> fingerprints = [];
        await foreach(Solution solution in evaluator.EvaluateAsync(TestContext.CancellationToken).ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>A two-pattern join over the overlapping term space.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern Join(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2)),
            ],
            registry);
    }

    [TestMethod]
    public async Task EveryGraphViewMatchesItsStandaloneIndex()
    {
        Dictionary<TermId, IEnumerable<EncodedTriple>> graphs = BuildGraphs(24);
        ColumnarGraphSetIndex set = ColumnarGraphSetIndex.Build(graphs);

        foreach((TermId graph, IEnumerable<EncodedTriple> triples) in graphs)
        {
            ColumnarTripleIndex? view = set.GetView(graph);
            Assert.IsNotNull(view);

            ColumnarTripleIndex standalone = ColumnarTripleIndex.Build(triples);

            //Walks agree triple for triple.
            List<EncodedTriple> viaView = [.. view.EnumerateTriples()];
            List<EncodedTriple> viaStandalone = [.. standalone.EnumerateTriples()];
            viaView.Sort(CompareTriples);
            viaStandalone.Sort(CompareTriples);
            Assert.AreSequenceEqual(viaStandalone, viaView);
            Assert.AreEqual(standalone.TripleCount, view.TripleCount);

            //Membership agrees, present and absent.
            foreach(EncodedTriple triple in viaStandalone)
            {
                Assert.IsTrue(view.Contains(triple.Subject, triple.Predicate, triple.Object));
            }

            Assert.IsFalse(view.Contains(TermId.FromEncoded(1), TermId.FromEncoded(2), TermId.FromEncoded(3)));

            //Joins agree.
            VariableRegistry viewRegistry = new();
            VariableRegistry standaloneRegistry = new();
            BasicGraphPattern viewQuery = Join(viewRegistry);
            BasicGraphPattern standaloneQuery = Join(standaloneRegistry);

            List<string> viewSolutions = await DrainAsync(
                new ColumnarBasicGraphPatternEvaluator(view, viewQuery, Planners.FirstOccurrence(viewQuery), TimeProvider.System)).ConfigureAwait(false);
            List<string> standaloneSolutions = await DrainAsync(
                new ColumnarBasicGraphPatternEvaluator(standalone, standaloneQuery, Planners.FirstOccurrence(standaloneQuery), TimeProvider.System)).ConfigureAwait(false);

            Assert.AreSequenceEqual(standaloneSolutions, viewSolutions);
        }
    }

    [TestMethod]
    public void AdjacentGraphsWithIdenticalContentStayIsolated()
    {
        //Two graphs with byte-identical triples sit adjacent in the
        //shared columns; each view must see exactly its own copy
        //once, not the neighbour's groups merged in.
        List<EncodedTriple> content =
        [
            EncodedTriple.FromEncoded(10, 20, 30),
            EncodedTriple.FromEncoded(10, 20, 31),
            EncodedTriple.FromEncoded(11, 21, 32),
        ];

        Dictionary<TermId, IEnumerable<EncodedTriple>> graphs = new()
        {
            [TermId.FromEncoded(500)] = content,
            [TermId.FromEncoded(501)] = content,
        };

        ColumnarGraphSetIndex set = ColumnarGraphSetIndex.Build(graphs);

        foreach(TermId graph in (TermId[])[TermId.FromEncoded(500), TermId.FromEncoded(501)])
        {
            ColumnarTripleIndex? view = set.GetView(graph);
            Assert.IsNotNull(view);
            Assert.AreEqual(3, view.TripleCount);
            Assert.HasCount(3, new List<EncodedTriple>(view.EnumerateTriples()));
        }
    }

    [TestMethod]
    public void ViewsMemoizeAndAbsentGraphsResolveToNull()
    {
        ColumnarGraphSetIndex set = ColumnarGraphSetIndex.Build(BuildGraphs(4));
        TermId graph = TermId.FromEncoded(10_000);

        ColumnarTripleIndex? first = set.GetView(graph);
        ColumnarTripleIndex? second = set.GetView(graph);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.IsNull(set.GetView(TermId.FromEncoded(999)));
    }

    [TestMethod]
    public void SharedColumnsUndercutPerGraphIndexes()
    {
        //Many small graphs: the per-graph indexes each pay block and
        //metadata floors per column per order; the shared columns
        //pay them once. The factor grows with graph count — assert a
        //conservative bound.
        Dictionary<TermId, IEnumerable<EncodedTriple>> graphs = BuildGraphs(256);
        ColumnarGraphSetIndex set = ColumnarGraphSetIndex.Build(graphs);

        long perGraphTotal = 0;
        foreach(IEnumerable<EncodedTriple> triples in graphs.Values)
        {
            ColumnarTripleIndex standalone = ColumnarTripleIndex.Build(triples);
            perGraphTotal += SumPackedBytes(standalone);
        }

        Assert.IsLessThan(perGraphTotal / 2, set.PackedByteCount);
    }

    /// <summary>Subject-predicate-object lexicographic comparison, for canonical walk ordering.</summary>
    /// <param name="left">The left triple.</param>
    /// <param name="right">The right triple.</param>
    /// <returns>The comparison result.</returns>
    private static int CompareTriples(EncodedTriple left, EncodedTriple right)
    {
        int bySubject = left.Subject.Encoded.CompareTo(right.Subject.Encoded);
        if(bySubject != 0)
        {
            return bySubject;
        }

        int byPredicate = left.Predicate.Encoded.CompareTo(right.Predicate.Encoded);

        return byPredicate != 0 ? byPredicate : left.Object.Encoded.CompareTo(right.Object.Encoded);
    }

    /// <summary>Sums a standalone index's packed bytes across its materialised orders.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The packed byte total.</returns>
    private static long SumPackedBytes(ColumnarTripleIndex index)
    {
        long total = 0;
        for(int i = 0; i < 6; i++)
        {
            if(index.IsPermutationAvailable(i))
            {
                total += index.OrderAt(i).PackedByteCount;
            }
        }

        return total;
    }
}
