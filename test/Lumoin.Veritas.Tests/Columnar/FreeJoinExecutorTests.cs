using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The Free Join generic join's contract (full-depth GHTs): its answers equal
/// the system-of-record leapfrog engine's on the same query, for both cyclic
/// shapes (the triangle — the worst-case-optimal case) and acyclic shapes (a
/// star and a chain). The generalized hash trie is the data structure; the
/// generic join is the hash-trie analogue of leapfrog, so the two must agree
/// tuple for tuple.
/// </summary>
[TestClass]
internal sealed class FreeJoinExecutorTests
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

    /// <summary>Drains a solution stream into order-insensitive per-solution fingerprints.</summary>
    /// <param name="solutions">The stream.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<Solution> solutions)
    {
        List<string> fingerprints = [];
        await foreach(Solution solution in solutions.ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Drains a Free Join batch stream into the same fingerprint form, sorting each row's cells by variable id.</summary>
    /// <param name="batches">The batch stream.</param>
    /// <param name="schema">The output schema, positional against the columns.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> Fingerprints(IEnumerable<SolutionBatch> batches, List<Variable> schema)
    {
        List<string> fingerprints = [];
        foreach(SolutionBatch batch in batches)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                List<string> cells = [];
                for(int column = 0; column < schema.Count; column++)
                {
                    cells.Add($"{schema[column].Id}={batch.ColumnOf(column)[row]}");
                }

                cells.Sort(StringComparer.Ordinal);
                fingerprints.Add(string.Join(";", cells));
            }
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Builds full-depth GHTs for every pattern (trie levels ordered by a global first-occurrence variable order) and runs the generic join, returning its fingerprints.</summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The Free Join result fingerprints.</returns>
    private static List<string> FreeJoinFingerprints(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        List<Variable> order = [];
        Dictionary<Variable, int> orderIndex = [];
        foreach(TriplePattern pattern in query.Patterns)
        {
            foreach(Variable variable in pattern.Variables())
            {
                if(orderIndex.TryAdd(variable, order.Count))
                {
                    order.Add(variable);
                }
            }
        }

        List<GeneralizedHashTrie> relations = [];
        foreach(TriplePattern pattern in query.Patterns)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, pattern);
            int[] trieColumns = [.. Enumerable.Range(0, scanSchema.Count).OrderBy(column => orderIndex[scanSchema[column]])];
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, pattern), trieColumns, []));
        }

        return Fingerprints(FreeJoinExecutor.Execute(relations, order), order);
    }

    /// <summary>Builds the index and the system-of-record store from the fixture, runs the query on both engines, and asserts the Free Join answers equal the leapfrog answers.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    private async Task AssertAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await DrainAsync(store.Query(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> freeJoin = FreeJoinFingerprints(index, query);

        Assert.IsGreaterThan(0, oracle.Count);
        CollectionAssert.AreEqual(oracle, freeJoin);
    }

    /// <summary>A directed graph over one predicate with some 3-cycles and some non-triangle edges.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> TriangleFixture()
    {
        HashSet<EncodedTriple> seen = [];
        List<EncodedTriple> triples = [];
        for(uint i = 0; i < 14; i++)
        {
            for(uint j = 0; j < 14; j++)
            {
                if(i != j && ((i * 7) + (j * 3)) % 5 == 0 && seen.Add(EncodedTriple.FromEncoded(i, 200, j)))
                {
                    triples.Add(EncodedTriple.FromEncoded(i, 200, j));
                }
            }
        }

        return triples;
    }

    /// <summary>The triangle query <c>?x p ?y . ?y p ?z . ?z p ?x</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern TriangleQuery(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(z), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(x)),
            ],
            registry);
    }

    /// <summary>A fan-out fixture: each subject carries several objects on three predicates (star) and the p1 objects carry a fourth predicate (chain).</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> AcyclicFixture()
    {
        List<EncodedTriple> triples = [];
        ulong state = 5;
        for(uint subject = 1; subject <= 30; subject++)
        {
            state = Mix(state);
            uint fan = 2 + (uint)(state % 4);
            for(uint k = 0; k < fan; k++)
            {
                uint o = 300 + ((subject * 10) + k);
                triples.Add(EncodedTriple.FromEncoded(subject, 200, o));
                triples.Add(EncodedTriple.FromEncoded(subject, 201, 400 + ((subject * 10) + k)));
                triples.Add(EncodedTriple.FromEncoded(subject, 202, 500 + ((subject * 10) + k)));
                triples.Add(EncodedTriple.FromEncoded(o, 203, 600 + ((subject * 10) + k)));
            }
        }

        return triples;
    }

    /// <summary>A three-pattern star on the shared subject <c>?s</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");
        Variable o3 = registry.GetOrAdd("o3");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(202)), PatternPosition.OfVariable(o3)),
            ],
            registry);
    }

    /// <summary>A two-pattern chain <c>?s p1 ?o . ?o p4 ?t</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable t = registry.GetOrAdd("t");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(o), PatternPosition.Bound(TermId.FromEncoded(203)), PatternPosition.OfVariable(t)),
            ],
            registry);
    }

    [TestMethod]
    public async Task TriangleAgreesWithLeapfrog()
    {
        await AssertAgreesWithLeapfrog(TriangleFixture(), TriangleQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task StarAgreesWithLeapfrog()
    {
        await AssertAgreesWithLeapfrog(AcyclicFixture(), StarQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainAgreesWithLeapfrog()
    {
        await AssertAgreesWithLeapfrog(AcyclicFixture(), ChainQuery).ConfigureAwait(false);
    }

    /// <summary>Builds a GHT per pattern at a chosen trie depth — the first <c>depth</c> of the pattern's scan columns in global order are trie levels, the rest leaf columns — and runs the generic join, returning its fingerprints.</summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="depths">The trie depth per pattern, parallel to <see cref="BasicGraphPattern.Patterns"/>.</param>
    /// <returns>The Free Join result fingerprints.</returns>
    private static List<string> FreeJoinFingerprintsAtDepth(ColumnarTripleIndex index, BasicGraphPattern query, int[] depths)
    {
        List<Variable> order = [];
        Dictionary<Variable, int> orderIndex = [];
        foreach(TriplePattern pattern in query.Patterns)
        {
            foreach(Variable variable in pattern.Variables())
            {
                if(orderIndex.TryAdd(variable, order.Count))
                {
                    order.Add(variable);
                }
            }
        }

        List<GeneralizedHashTrie> relations = [];
        for(int p = 0; p < query.Patterns.Count; p++)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, query.Patterns[p]);
            int[] byGlobal = [.. Enumerable.Range(0, scanSchema.Count).OrderBy(column => orderIndex[scanSchema[column]])];
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, query.Patterns[p]), byGlobal[..depths[p]], byGlobal[depths[p]..]));
        }

        return Fingerprints(FreeJoinExecutor.Execute(relations, order), order);
    }

    /// <summary>Runs the query on the leapfrog oracle and on the Free Join generic join with GHTs built at the given per-pattern depths, asserting the answers agree.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    /// <param name="depths">The trie depth per pattern.</param>
    private async Task AssertFreeJoinAtDepthAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, int[] depths)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await DrainAsync(store.Query(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> freeJoin = FreeJoinFingerprintsAtDepth(index, query, depths);

        Assert.IsGreaterThan(0, oracle.Count);
        CollectionAssert.AreEqual(oracle, freeJoin);
    }

    [TestMethod]
    public async Task StarAtDepthOneAgreesWithLeapfrog()
    {
        //Each pattern at depth one: a trie on the shared subject, the private
        //object carried as a leaf vector — the binary-join shape.
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(AcyclicFixture(), StarQuery, [1, 1, 1]).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainWithLeafTerminalAgreesWithLeapfrog()
    {
        //The shared ?o stays a trie level in both relations; only the private
        //terminal ?t of the second relation drops into a leaf vector.
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(AcyclicFixture(), ChainQuery, [2, 1]).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainAllDepthOneAgreesWithLeapfrog()
    {
        //Both relations at depth one: ?o is the first relation's leaf yet the
        //second's trie key, so the ?o level intersects a leaf with a trie node —
        //the shared-leaf-meets-trie case.
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(AcyclicFixture(), ChainQuery, [1, 1]).ConfigureAwait(false);
    }

    /// <summary>A diamond <c>?s p0 ?a . ?s p1 ?b . ?a p2 ?z . ?b p3 ?z</c> where each <c>?s</c> reaches a shared set of <c>?z</c> through both <c>?a</c> and <c>?b</c>.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> DiamondFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 10; subject++)
        {
            uint a = 100 + subject;
            uint b = 200 + subject;
            triples.Add(EncodedTriple.FromEncoded(subject, 200, a));
            triples.Add(EncodedTriple.FromEncoded(subject, 201, b));
            for(uint k = 0; k < 3; k++)
            {
                uint z = 300 + (subject * 3) + k;
                triples.Add(EncodedTriple.FromEncoded(a, 202, z));
                triples.Add(EncodedTriple.FromEncoded(b, 203, z));
            }
        }

        return triples;
    }

    /// <summary>The diamond query <c>?s p0 ?a . ?s p1 ?b . ?a p2 ?z . ?b p3 ?z</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern DiamondQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(a)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(TermId.FromEncoded(202)), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(TermId.FromEncoded(203)), PatternPosition.OfVariable(z)),
            ],
            registry);
    }

    [TestMethod]
    public async Task DiamondSharedLeafAgreesWithLeapfrog()
    {
        //?z is a leaf column of both ?a-p2-?z and ?b-p3-?z and a trie level in
        //neither, so its level is a pure leaf-leaf intersection — two leaf
        //participants, no trie participant.
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(DiamondFixture(), DiamondQuery, [2, 2, 1, 1]).ConfigureAwait(false);
    }
}
