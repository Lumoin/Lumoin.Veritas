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
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The Free Join generic join's contract: its answers equal the
/// system-of-record leapfrog engine's on the same query, for both cyclic
/// shapes (the triangle — the worst-case-optimal case) and acyclic shapes (a
/// star and a chain), at full depth and at the shallower depths that carry a
/// relation's tail in a leaf vector. The generalized hash trie is the data
/// structure; the generic join is the hash-trie analogue of leapfrog, so the
/// two must agree tuple for tuple. The factorised emission carries the same
/// obligation across depths: a relation's level-1 extent is a hash node at
/// full depth and a leaf tuple vector at cover depth, and the batch it
/// produces must be identical either way.
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
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <returns>The Free Join result fingerprints.</returns>
    private static List<string> FreeJoinFingerprints(ColumnarTripleIndex index, BasicGraphPattern query, FreeJoinTrieBuild trieBuild)
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
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, pattern), trieColumns, [], trieBuild));
        }

        return Fingerprints(FreeJoinExecutor.Execute(relations, order), order);
    }

    /// <summary>Builds the index and the system-of-record store from the fixture, runs the query on both engines, and asserts the Free Join answers equal the leapfrog answers.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps; eager by default.</param>
    private async Task AssertAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await DrainAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> freeJoin = FreeJoinFingerprints(index, query, trieBuild);

        Assert.IsGreaterThan(0, oracle.Count);
        Assert.AreSequenceEqual(oracle, freeJoin);
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

    [TestMethod]
    public async Task TriangleWithLazyTriesAgreesWithLeapfrog()
    {
        //Full-depth lazy tries force level by level as the worst-case-optimal
        //descent touches them; the answers must not depend on the build mode.
        await AssertAgreesWithLeapfrog(TriangleFixture(), TriangleQuery, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    /// <summary>Builds a GHT per pattern at a chosen trie depth — the first <c>depth</c> of the pattern's scan columns in global order are trie levels, the rest leaf columns — and runs the generic join, returning its fingerprints.</summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="depths">The trie depth per pattern, parallel to <see cref="BasicGraphPattern.Patterns"/>.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <returns>The Free Join result fingerprints.</returns>
    private static List<string> FreeJoinFingerprintsAtDepth(ColumnarTripleIndex index, BasicGraphPattern query, int[] depths, FreeJoinTrieBuild trieBuild)
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
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, query.Patterns[p]), byGlobal[..depths[p]], byGlobal[depths[p]..], trieBuild));
        }

        return Fingerprints(FreeJoinExecutor.Execute(relations, order), order);
    }

    /// <summary>Runs the query on the leapfrog oracle and on the Free Join generic join with GHTs built at the given per-pattern depths, asserting the answers agree.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    /// <param name="depths">The trie depth per pattern.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps; eager by default.</param>
    private async Task AssertFreeJoinAtDepthAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, int[] depths, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await DrainAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> freeJoin = FreeJoinFingerprintsAtDepth(index, query, depths, trieBuild);

        Assert.IsGreaterThan(0, oracle.Count);
        Assert.AreSequenceEqual(oracle, freeJoin);
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

    [TestMethod]
    public async Task StarAtDepthOneWithLazyTriesAgreesWithLeapfrog()
    {
        //The binary-join shape under the lazy build: each root force is the
        //one hash pass, and the leaves read through the column store.
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(AcyclicFixture(), StarQuery, [1, 1, 1], FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainWithLeafTerminalAndLazyTriesAgreesWithLeapfrog()
    {
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(AcyclicFixture(), ChainQuery, [2, 1], FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainAllDepthOneWithLazyTriesAgreesWithLeapfrog()
    {
        //The shared-leaf-meets-trie case with the leaf side read through the
        //lazy column store instead of a packed vector.
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(AcyclicFixture(), ChainQuery, [1, 1], FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DiamondSharedLeafWithLazyTriesAgreesWithLeapfrog()
    {
        //The pure leaf-leaf intersection with both leaf participants reading
        //through their lazy column stores.
        await AssertFreeJoinAtDepthAgreesWithLeapfrog(DiamondFixture(), DiamondQuery, [2, 2, 1, 1], FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    /// <summary>Builds a GHT per pattern at a chosen trie depth over a GIVEN global order — the factorised order's key-first form, which first-occurrence ordering does not reproduce.</summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="order">The global descent order the trie levels follow.</param>
    /// <param name="depths">The trie depth per pattern, parallel to <see cref="BasicGraphPattern.Patterns"/>.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <returns>The relations, parallel to the query's patterns.</returns>
    private static List<GeneralizedHashTrie> RelationsAtDepths(ColumnarTripleIndex index, BasicGraphPattern query, List<Variable> order, int[] depths, FreeJoinTrieBuild trieBuild)
    {
        Dictionary<Variable, int> orderIndex = new(order.Count);
        for(int k = 0; k < order.Count; k++)
        {
            orderIndex[order[k]] = k;
        }

        List<GeneralizedHashTrie> relations = new(query.Patterns.Count);
        for(int pattern = 0; pattern < query.Patterns.Count; pattern++)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, query.Patterns[pattern]);
            int[] byGlobal = FreeJoinPipeline.OrderedColumns(scanSchema, orderIndex);
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, query.Patterns[pattern]), byGlobal[..depths[pattern]], byGlobal[depths[pattern]..], trieBuild));
        }

        return relations;
    }

    /// <summary>
    /// The factorised emission's cross-depth oracle: the batch built from
    /// cover-depth relations flattens to the leapfrog answers, to the
    /// full-depth batch's flattening, and to the generic join driven over the
    /// same cover-depth relations, with the compression figures — group count,
    /// flat row count, stored tuple count — identical at both depths.
    /// </summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over the shared registry.</param>
    /// <param name="orderNames">The factorised order's variable names, key first.</param>
    /// <param name="coverDepths">The join-cover trie depth per pattern.</param>
    /// <param name="fullDepths">The full trie depth per pattern.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps; eager by default.</param>
    /// <param name="expectedGroups">The hand-computed group count both depths must report, or <see langword="null"/> to leave it to the cross-depth equality.</param>
    private async Task AssertFactorizedAtDepthsAgree(
        List<EncodedTriple> fixture,
        Func<VariableRegistry, BasicGraphPattern> queryBuilder,
        string[] orderNames,
        int[] coverDepths,
        int[] fullDepths,
        FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager,
        int? expectedGroups = null)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        BasicGraphPattern query = queryBuilder(registry);

        List<Variable> order = [];
        foreach(string name in orderNames)
        {
            order.Add(registry.GetOrAdd(name));
        }

        List<string> oracle = await DrainAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        using FactorizedArena coverArena = new(VeritasMemoryPool<uint>.Shared);
        using FactorizedArena fullArena = new(VeritasMemoryPool<uint>.Shared);

        FactorizedBatch? cover = FreeJoinExecutor.ExecuteFactorized(RelationsAtDepths(index, query, order, coverDepths, trieBuild), order, coverArena);
        FactorizedBatch? full = FreeJoinExecutor.ExecuteFactorized(RelationsAtDepths(index, query, order, fullDepths, trieBuild), order, fullArena);

        Assert.IsNotNull(cover, "The executor declined the cover-depth relations.");
        Assert.IsNotNull(full, "The executor declined the full-depth relations.");

        //A lazy trie's navigation mutates it, so the generic-join drive takes
        //its own relations rather than the ones the emission already walked.
        List<string> driven = Fingerprints(FreeJoinExecutor.Execute(RelationsAtDepths(index, query, order, coverDepths, trieBuild), order), order);

        Assert.IsGreaterThan(0, oracle.Count);
        Assert.AreSequenceEqual(oracle, Fingerprints(cover.Flatten(), order));
        Assert.AreSequenceEqual(oracle, Fingerprints(full.Flatten(), order));
        Assert.AreSequenceEqual(oracle, driven);

        Assert.HasCount(full.Groups.Count, cover.Groups);
        Assert.AreEqual(full.FlatRowCount, cover.FlatRowCount);
        Assert.AreEqual(full.FactorizedTupleCount, cover.FactorizedTupleCount);

        if(expectedGroups is not null)
        {
            Assert.HasCount(expectedGroups.Value, cover.Groups);
            Assert.HasCount(expectedGroups.Value, full.Groups);
        }
    }

    /// <summary>Packs hand-written rows into <see cref="SolutionBatch"/>es over the schema — the relation source for shapes the routed scan cannot produce, such as a literally duplicated row.</summary>
    /// <param name="schema">The relation schema, positional against each row's values.</param>
    /// <param name="rows">The rows, each carrying one value per schema column.</param>
    /// <returns>The batch stream.</returns>
    private static List<SolutionBatch> HandBuiltBatches(List<Variable> schema, uint[][] rows)
    {
        List<SolutionBatch> batches = [];
        SolutionBatch batch = new(schema);
        int filled = 0;
        foreach(uint[] row in rows)
        {
            for(int column = 0; column < schema.Count; column++)
            {
                batch.ColumnSpan(column)[filled] = row[column];
            }

            filled++;

            if(filled == SolutionBatch.BatchLength)
            {
                batch.SetCount(filled);
                batches.Add(batch);
                batch = new SolutionBatch(schema);
                filled = 0;
            }
        }

        if(filled > 0)
        {
            batch.SetCount(filled);
            batches.Add(batch);
        }

        return batches;
    }

    /// <summary>Builds one hand-written relation at a chosen trie depth: the leading <paramref name="depth"/> schema columns are trie levels, the rest leaf columns, the schema already written in global order.</summary>
    /// <param name="schema">The relation schema, in global order.</param>
    /// <param name="rows">The relation's rows.</param>
    /// <param name="depth">The trie depth.</param>
    /// <param name="trieBuild">How the trie materialises its maps.</param>
    /// <returns>The relation.</returns>
    private static GeneralizedHashTrie HandBuiltRelation(List<Variable> schema, uint[][] rows, int depth, FreeJoinTrieBuild trieBuild)
    {
        int[] columns = [.. Enumerable.Range(0, schema.Count)];

        return GeneralizedHashTrie.Build(schema, HandBuiltBatches(schema, rows), columns[..depth], columns[depth..], trieBuild);
    }

    /// <summary>The fingerprint form of hand-written expected rows, produced through the same cell layout <see cref="Fingerprints"/> uses so the two are directly comparable.</summary>
    /// <param name="rows">The expected rows, each carrying one value per order position.</param>
    /// <param name="schema">The output schema, positional against each row's values.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> ExpectedFingerprints(uint[][] rows, List<Variable> schema)
    {
        return Fingerprints(HandBuiltBatches(schema, rows), schema);
    }

    /// <summary>A star-with-chain fixture: each subject fans out on two star arms and a third arm whose objects each fan out one hop further — the hybrid that puts a full-depth extended centre beside cover-depth centres.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarChainHybridFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 4; subject++)
        {
            for(uint j = 0; j < 2; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, 220, 1_000 + (subject * 10) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, 221, 2_000 + (subject * 10) + j));
            }

            for(uint j = 0; j < 2; j++)
            {
                uint branchValue = 3_000 + (subject * 10) + j;
                triples.Add(EncodedTriple.FromEncoded(subject, 222, branchValue));
                for(uint c = 0; c < 2; c++)
                {
                    triples.Add(EncodedTriple.FromEncoded(branchValue, 223, 4_000 + (((subject * 10) + j) * 10) + c));
                }
            }
        }

        return triples;
    }

    /// <summary>The hybrid query <c>?s p0 ?o0 . ?s p1 ?o1 . ?s p2 ?b . ?b p3 ?c</c> — a three-arm star whose third arm is extended one hop.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarChainHybridQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o0 = registry.GetOrAdd("o0");
        Variable o1 = registry.GetOrAdd("o1");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(220)), PatternPosition.OfVariable(o0)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(221)), PatternPosition.OfVariable(o1)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(222)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(TermId.FromEncoded(223)), PatternPosition.OfVariable(c)),
            ],
            registry);
    }

    /// <summary>
    /// A star whose three arms hold overlapping but different subject sets:
    /// the first stops at four, the second holds one, two, three and five, and
    /// the third holds all six. Neither of the two smallest roots contains the
    /// other, so whichever arm drives the key scan meets a key another arm
    /// does not hold — the presence lookup must actually fail for the row to
    /// mean anything — and exactly three subjects survive the intersection.
    /// </summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> PartialStarFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 6; subject++)
        {
            for(uint j = 0; j < 2; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, 242, 300 + (subject * 10) + j));

                if(subject <= 4)
                {
                    triples.Add(EncodedTriple.FromEncoded(subject, 240, 100 + (subject * 10) + j));
                }

                if(subject <= 3 || subject == 5)
                {
                    triples.Add(EncodedTriple.FromEncoded(subject, 241, 200 + (subject * 10) + j));
                }
            }
        }

        return triples;
    }

    /// <summary>The partial star query <c>?s p0 ?o0 . ?s p1 ?o1 . ?s p2 ?o2</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern PartialStarQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o0 = registry.GetOrAdd("o0");
        Variable o1 = registry.GetOrAdd("o1");
        Variable o2 = registry.GetOrAdd("o2");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(240)), PatternPosition.OfVariable(o0)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(241)), PatternPosition.OfVariable(o1)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(242)), PatternPosition.OfVariable(o2)),
            ],
            registry);
    }

    /// <summary>
    /// A hybrid fixture whose extension is partial: subject 1 has one extended
    /// branch value and one with no <c>?c</c>, subject 2's every branch value
    /// has none (its group dies), and subject 3's single branch value is fully
    /// extended.
    /// </summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> PartialHybridFixture()
    {
        List<EncodedTriple> triples =
        [
            EncodedTriple.FromEncoded(1, 250, 1_001),
            EncodedTriple.FromEncoded(1, 251, 2_001),
            EncodedTriple.FromEncoded(1, 252, 3_011),
            EncodedTriple.FromEncoded(1, 252, 3_012),
            EncodedTriple.FromEncoded(3_011, 253, 4_001),
            EncodedTriple.FromEncoded(3_011, 253, 4_002),

            EncodedTriple.FromEncoded(2, 250, 1_002),
            EncodedTriple.FromEncoded(2, 251, 2_002),
            EncodedTriple.FromEncoded(2, 252, 3_021),
            EncodedTriple.FromEncoded(2, 252, 3_022),

            EncodedTriple.FromEncoded(3, 250, 1_003),
            EncodedTriple.FromEncoded(3, 251, 2_003),
            EncodedTriple.FromEncoded(3, 252, 3_031),
            EncodedTriple.FromEncoded(3_031, 253, 4_003)
        ];

        return triples;
    }

    /// <summary>The partial hybrid query <c>?s p0 ?o0 . ?s p1 ?o1 . ?s p2 ?b . ?b p3 ?c</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern PartialHybridQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o0 = registry.GetOrAdd("o0");
        Variable o1 = registry.GetOrAdd("o1");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(250)), PatternPosition.OfVariable(o0)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(251)), PatternPosition.OfVariable(o1)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(252)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(TermId.FromEncoded(253)), PatternPosition.OfVariable(c)),
            ],
            registry);
    }

    [TestMethod]
    public async Task FactorizedStarAtCoverDepthsAgreesAtEveryOracle()
    {
        //Every arm at cover depth one: the key is the only trie level and each
        //branch's values live in a leaf vector, so all three branch extents are
        //read through the leaf reader instead of a level-1 node.
        await AssertFactorizedAtDepthsAgree(AcyclicFixture(), StarQuery, ["s", "o", "o2", "o3"], [1, 1, 1], [2, 2, 2]).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FactorizedStarChainAtMixedCoverDepthsAgreesAtEveryOracle()
    {
        //The increment's signature vector: two leaf-sourced centres, the
        //extended centre held at full depth because its branch is a join
        //variable, and a leaf-sourced extension under it.
        await AssertFactorizedAtDepthsAgree(StarChainHybridFixture(), StarChainHybridQuery, ["s", "o0", "o1", "b", "c"], [1, 1, 2, 1], [2, 2, 2, 2]).ConfigureAwait(false);
    }

    [TestMethod]
    public void FactorizedCoverDepthCollapsesDuplicateBranchRows()
    {
        VariableRegistry registry = new();
        Variable k = registry.GetOrAdd("k");
        Variable b0 = registry.GetOrAdd("b0");
        Variable b1 = registry.GetOrAdd("b1");
        List<Variable> order = [k, b0, b1];
        List<Variable> firstSchema = [k, b0];
        List<Variable> secondSchema = [k, b1];

        //The first centre carries (1, 10) twice — a bag the routed scan cannot
        //produce, since it projects distinct triples. A trie level collapses
        //the pair structurally and a leaf vector keeps both, so the emission's
        //distinct pass is what makes the two depths agree. Hand model: k=1
        //stands for {10,11} × {20,21} and k=2 for {12} × {22} — five flat rows
        //in two groups over six stored values.
        uint[][] firstRows = [[1, 10], [1, 10], [1, 11], [2, 12]];
        uint[][] secondRows = [[1, 20], [1, 21], [2, 22]];
        uint[][] expectedRows = [[1, 10, 20], [1, 10, 21], [1, 11, 20], [1, 11, 21], [2, 12, 22]];
        List<string> expected = ExpectedFingerprints(expectedRows, order);

        using FactorizedArena coverArena = new(VeritasMemoryPool<uint>.Shared);
        using FactorizedArena fullArena = new(VeritasMemoryPool<uint>.Shared);

        FactorizedBatch? cover = FreeJoinExecutor.ExecuteFactorized(
            [
                HandBuiltRelation(firstSchema, firstRows, 1, FreeJoinTrieBuild.Eager),
                HandBuiltRelation(secondSchema, secondRows, 1, FreeJoinTrieBuild.Eager)
            ],
            order,
            coverArena);
        FactorizedBatch? full = FreeJoinExecutor.ExecuteFactorized(
            [
                HandBuiltRelation(firstSchema, firstRows, 2, FreeJoinTrieBuild.Eager),
                HandBuiltRelation(secondSchema, secondRows, 2, FreeJoinTrieBuild.Eager)
            ],
            order,
            fullArena);

        Assert.IsNotNull(cover, "The executor declined the cover-depth relations.");
        Assert.IsNotNull(full, "The executor declined the full-depth relations.");

        Assert.AreSequenceEqual(expected, Fingerprints(cover.Flatten(), order));
        Assert.AreSequenceEqual(expected, Fingerprints(full.Flatten(), order));
        Assert.HasCount(2, cover.Groups);
        Assert.HasCount(2, full.Groups);
        Assert.AreEqual(5L, cover.FlatRowCount);
        Assert.AreEqual(5L, full.FlatRowCount);
        Assert.AreEqual(6L, cover.FactorizedTupleCount);
        Assert.AreEqual(6L, full.FactorizedTupleCount);
    }

    [TestMethod]
    public void FactorizedDeclinesARelationWiderThanTwoColumns()
    {
        VariableRegistry registry = new();
        Variable k = registry.GetOrAdd("k");
        Variable b0 = registry.GetOrAdd("b0");
        Variable b1 = registry.GetOrAdd("b1");
        List<Variable> order = [k, b0, b1];
        List<Variable> wideSchema = [k, b0, b1];
        List<Variable> narrowSchema = [k, b0];

        //Three columns split into two trie levels and one leaf column. The
        //acceptance is two columns HOWEVER split, not one leaf column at any
        //width, so the wider relation is refused outright.
        uint[][] wideRows = [[1, 10, 20], [1, 11, 21], [2, 12, 22]];
        uint[][] narrowRows = [[1, 10], [2, 12]];

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);

        Assert.IsNull(FreeJoinExecutor.ExecuteFactorized(
            [
                HandBuiltRelation(wideSchema, wideRows, 2, FreeJoinTrieBuild.Eager),
                HandBuiltRelation(narrowSchema, narrowRows, 2, FreeJoinTrieBuild.Eager)
            ],
            order,
            arena));
    }

    [TestMethod]
    public async Task FactorizedStarChainAtCoverDepthsWithLazyTriesAgrees()
    {
        //The same mixed vector with every map materialised on first touch: a
        //depth-1 relation's root force builds every leaf row subset, and the
        //emission reads those subsets through the column store.
        await AssertFactorizedAtDepthsAgree(StarChainHybridFixture(), StarChainHybridQuery, ["s", "o0", "o1", "b", "c"], [1, 1, 2, 1], [2, 2, 2, 2], FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FactorizedCoverDepthDropsAKeyMissingFromOneCentre()
    {
        //The driving arm meets a key another arm does not hold, and the
        //intersection keeps exactly the three subjects all three share, at
        //depth one as at depth two: a root key maps to a leaf id only where a
        //row landed in it, so a missing key fails the lookup either way.
        await AssertFactorizedAtDepthsAgree(PartialStarFixture(), PartialStarQuery, ["s", "o0", "o1", "o2"], [1, 1, 1], [2, 2, 2], expectedGroups: 3).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FactorizedCoverDepthDropsABranchValueWithNoExtensionMatch()
    {
        //Subject 1 keeps only its extended branch value, subject 2 has none
        //and dies, subject 3 keeps its single one: two groups survive the
        //semijoin at both depths.
        await AssertFactorizedAtDepthsAgree(PartialHybridFixture(), PartialHybridQuery, ["s", "o0", "o1", "b", "c"], [1, 1, 2, 1], [2, 2, 2, 2], expectedGroups: 2).ConfigureAwait(false);
    }

    [TestMethod]
    public void FactorizedCoverDepthCollapsesDuplicateExtensionRows()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable b0 = registry.GetOrAdd("b0");
        Variable b1 = registry.GetOrAdd("b1");
        Variable c = registry.GetOrAdd("c");
        List<Variable> order = [s, b0, b1, c];
        List<Variable> firstSchema = [s, b0];
        List<Variable> secondSchema = [s, b1];
        List<Variable> extensionSchema = [b0, c];

        //The duplicate sits in the EXTENSION, whose values are written at the
        //nested-extension fill — a site the flat-branch row never reaches.
        //Hand model: s=1 nests b0=10 over {30,31} and b0=11 over {32} beside
        //the flat b1={20}, s=2 nests b0=12 over {33} beside b1={21} — four
        //flat rows in two groups over six stored values.
        uint[][] firstRows = [[1, 10], [1, 11], [2, 12]];
        uint[][] secondRows = [[1, 20], [2, 21]];
        uint[][] extensionRows = [[10, 30], [10, 30], [10, 31], [11, 32], [12, 33]];
        uint[][] expectedRows = [[1, 10, 20, 30], [1, 10, 20, 31], [1, 11, 20, 32], [2, 12, 21, 33]];
        List<string> expected = ExpectedFingerprints(expectedRows, order);

        using FactorizedArena coverArena = new(VeritasMemoryPool<uint>.Shared);
        using FactorizedArena fullArena = new(VeritasMemoryPool<uint>.Shared);

        FactorizedBatch? cover = FreeJoinExecutor.ExecuteFactorized(
            [
                HandBuiltRelation(firstSchema, firstRows, 2, FreeJoinTrieBuild.Eager),
                HandBuiltRelation(secondSchema, secondRows, 1, FreeJoinTrieBuild.Eager),
                HandBuiltRelation(extensionSchema, extensionRows, 1, FreeJoinTrieBuild.Eager)
            ],
            order,
            coverArena);
        FactorizedBatch? full = FreeJoinExecutor.ExecuteFactorized(
            [
                HandBuiltRelation(firstSchema, firstRows, 2, FreeJoinTrieBuild.Eager),
                HandBuiltRelation(secondSchema, secondRows, 2, FreeJoinTrieBuild.Eager),
                HandBuiltRelation(extensionSchema, extensionRows, 2, FreeJoinTrieBuild.Eager)
            ],
            order,
            fullArena);

        Assert.IsNotNull(cover, "The executor declined the cover-depth relations.");
        Assert.IsNotNull(full, "The executor declined the full-depth relations.");

        Assert.AreSequenceEqual(expected, Fingerprints(cover.Flatten(), order));
        Assert.AreSequenceEqual(expected, Fingerprints(full.Flatten(), order));
        Assert.HasCount(2, cover.Groups);
        Assert.HasCount(2, full.Groups);
        Assert.AreEqual(4L, cover.FlatRowCount);
        Assert.AreEqual(4L, full.FlatRowCount);
        Assert.AreEqual(6L, cover.FactorizedTupleCount);
        Assert.AreEqual(6L, full.FactorizedTupleCount);
    }
}
