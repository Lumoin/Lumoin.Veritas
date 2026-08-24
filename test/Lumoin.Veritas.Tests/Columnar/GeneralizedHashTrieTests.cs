using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The generalized hash trie's lossless contract: for any trie/leaf column
/// split and any column order, flattening the built trie reproduces the input
/// relation exactly. The flatten is the differential oracle the Free Join
/// generic join will be built on, so the trie must lose nothing — across a
/// full-depth trie (no leaf columns), a binary-join-shaped depth-1 trie (most
/// columns in the leaf), and reordered trie levels. The footprint diagnostics
/// carry their own rows: the node side counts every materialised map entry
/// without forcing a lazy map, and the value side counts leaf values only, so a
/// full-depth trie's zero there is the absence of leaf columns rather than the
/// absence of footprint.
/// </summary>
[TestClass]
internal sealed class GeneralizedHashTrieTests
{
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

    /// <summary>A three-column schema.</summary>
    /// <returns>The schema.</returns>
    private static IReadOnlyList<Variable> ThreeColumnSchema()
    {
        VariableRegistry registry = new();

        return [registry.GetOrAdd("a"), registry.GetOrAdd("b"), registry.GetOrAdd("c")];
    }

    /// <summary>A relation of distinct three-column rows, fanning out so the trie branches and leaves group.</summary>
    /// <param name="count">The target distinct row count.</param>
    /// <returns>The distinct rows.</returns>
    private static List<(uint A, uint B, uint C)> DistinctRows(int count)
    {
        HashSet<(uint, uint, uint)> seen = [];
        List<(uint A, uint B, uint C)> rows = [];
        //The value space (12 × 16 × 24 = 4608 distinct triples) stays well above
        //the requested count so the distinct-row loop always terminates, while
        //the small per-level fan-out makes the trie branch and the leaves group.
        ulong state = 11;
        while(rows.Count < count)
        {
            state = Mix(state);
            uint a = 10 + (uint)(state % 12);
            uint b = 100 + (uint)((state >> 16) % 16);
            uint c = 1_000 + (uint)((state >> 32) % 24);
            if(seen.Add((a, b, c)))
            {
                rows.Add((a, b, c));
            }
        }

        return rows;
    }

    /// <summary>Packs the three-column rows into <see cref="SolutionBatch"/>es of at most <see cref="SolutionBatch.BatchLength"/> rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="schema">The three-column schema.</param>
    /// <returns>The batch stream.</returns>
    private static List<SolutionBatch> BuildBatches(List<(uint A, uint B, uint C)> rows, IReadOnlyList<Variable> schema)
    {
        List<SolutionBatch> batches = [];
        SolutionBatch batch = new(schema);
        int filled = 0;
        foreach((uint a, uint b, uint c) in rows)
        {
            batch.ColumnSpan(0)[filled] = a;
            batch.ColumnSpan(1)[filled] = b;
            batch.ColumnSpan(2)[filled] = c;
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

    /// <summary>Drains a batch stream into order-insensitive per-row fingerprints over the schema.</summary>
    /// <param name="batches">The batch stream.</param>
    /// <param name="schema">The schema, positional against the columns.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> Fingerprints(IEnumerable<SolutionBatch> batches, IReadOnlyList<Variable> schema)
    {
        List<string> rows = [];
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
                rows.Add(string.Join(";", cells));
            }
        }

        rows.Sort(StringComparer.Ordinal);

        return rows;
    }

    /// <summary>Builds a trie over the given split in the given mode and asserts its flatten reproduces the input relation.</summary>
    /// <param name="trieColumns">The trie-level column indices.</param>
    /// <param name="leafColumns">The leaf column indices.</param>
    /// <param name="trieBuild">How the trie materialises its maps; eager by default.</param>
    private static void AssertLosslessSplit(int[] trieColumns, int[] leafColumns, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        IReadOnlyList<Variable> schema = ThreeColumnSchema();
        List<(uint, uint, uint)> rows = DistinctRows(2_500);
        List<SolutionBatch> batches = BuildBatches(rows, schema);

        List<string> expected = Fingerprints(batches, schema);

        GeneralizedHashTrie trie = GeneralizedHashTrie.Build(schema, batches, trieColumns, leafColumns, trieBuild);
        List<string> flattened = Fingerprints(trie.Flatten(), schema);

        Assert.IsGreaterThan(0, expected.Count);
        Assert.AreSequenceEqual(expected, flattened);
    }

    /// <summary>A two-column schema.</summary>
    /// <returns>The schema.</returns>
    private static IReadOnlyList<Variable> TwoColumnSchema()
    {
        VariableRegistry registry = new();

        return [registry.GetOrAdd("a"), registry.GetOrAdd("b")];
    }

    /// <summary>
    /// A hand-built two-column relation whose node-entry arithmetic is read off
    /// the fixture: two distinct first-column values, each carrying two distinct
    /// second-column values, over six rows with repeats so the leaves group.
    /// </summary>
    /// <param name="schema">The two-column schema.</param>
    /// <returns>The single-batch relation.</returns>
    private static List<SolutionBatch> NodeEntryFixture(IReadOnlyList<Variable> schema)
    {
        SolutionBatch batch = new(schema);
        Span<uint> a = batch.ColumnSpan(0);
        Span<uint> b = batch.ColumnSpan(1);
        a[0] = 1; b[0] = 10;
        a[1] = 1; b[1] = 10;
        a[2] = 1; b[2] = 11;
        a[3] = 2; b[3] = 20;
        a[4] = 2; b[4] = 21;
        a[5] = 2; b[5] = 21;
        batch.SetCount(6);

        return [batch];
    }

    [TestMethod]
    public void FullDepthTrieFlattensLosslessly()
    {
        //Every column is a trie level; the leaves are bare terminals.
        AssertLosslessSplit(trieColumns: [0, 1, 2], leafColumns: []);
    }

    [TestMethod]
    public void DepthTwoTrieWithALeafColumnFlattensLosslessly()
    {
        AssertLosslessSplit(trieColumns: [0, 1], leafColumns: [2]);
    }

    [TestMethod]
    public void DepthOneTrieIsABinaryJoinShapeAndFlattensLosslessly()
    {
        //One hash level then a leaf tuple vector — the binary hash join shape.
        AssertLosslessSplit(trieColumns: [0], leafColumns: [1, 2]);
    }

    [TestMethod]
    public void ReorderedTrieLevelsFlattenLosslessly()
    {
        //The trie order need not follow the schema order.
        AssertLosslessSplit(trieColumns: [2, 0], leafColumns: [1]);
    }

    [TestMethod]
    public void EmptyRelationFlattensToNothing()
    {
        IReadOnlyList<Variable> schema = ThreeColumnSchema();
        GeneralizedHashTrie trie = GeneralizedHashTrie.Build(schema, [], trieColumns: [0, 1], leafColumns: [2]);

        Assert.IsEmpty(Fingerprints(trie.Flatten(), schema));
    }

    [TestMethod]
    public void LazyFullDepthTrieFlattensLosslessly()
    {
        AssertLosslessSplit(trieColumns: [0, 1, 2], leafColumns: [], FreeJoinTrieBuild.Lazy);
    }

    [TestMethod]
    public void LazyDepthTwoTrieWithALeafColumnFlattensLosslessly()
    {
        AssertLosslessSplit(trieColumns: [0, 1], leafColumns: [2], FreeJoinTrieBuild.Lazy);
    }

    [TestMethod]
    public void LazyDepthOneTrieIsABinaryJoinShapeAndFlattensLosslessly()
    {
        AssertLosslessSplit(trieColumns: [0], leafColumns: [1, 2], FreeJoinTrieBuild.Lazy);
    }

    [TestMethod]
    public void LazyReorderedTrieLevelsFlattenLosslessly()
    {
        AssertLosslessSplit(trieColumns: [2, 0], leafColumns: [1], FreeJoinTrieBuild.Lazy);
    }

    [TestMethod]
    public void LazyEmptyRelationFlattensToNothing()
    {
        IReadOnlyList<Variable> schema = ThreeColumnSchema();
        GeneralizedHashTrie trie = GeneralizedHashTrie.Build(schema, [], trieColumns: [0, 1], leafColumns: [2], FreeJoinTrieBuild.Lazy);

        Assert.IsEmpty(Fingerprints(trie.Flatten(), schema));
    }

    [TestMethod]
    public void DuplicateRowsKeepTheirMultiplicityInBothBuildModes()
    {
        //Every generated fixture dedups whole rows, so the leaf-multiplicity
        //contract needs its own corpus: a literally repeated row must flatten
        //twice from the eager packed leaf and from the lazy row subset alike.
        IReadOnlyList<Variable> schema = ThreeColumnSchema();
        SolutionBatch batch = new(schema);
        Span<uint> a = batch.ColumnSpan(0);
        Span<uint> b = batch.ColumnSpan(1);
        Span<uint> c = batch.ColumnSpan(2);
        a[0] = 7; b[0] = 70; c[0] = 700;
        a[1] = 7; b[1] = 70; c[1] = 700;
        a[2] = 7; b[2] = 71; c[2] = 701;
        batch.SetCount(3);
        List<SolutionBatch> batches = [batch];

        List<string> expected = Fingerprints(batches, schema);

        List<string> eagerRows = Fingerprints(GeneralizedHashTrie.Build(schema, batches, trieColumns: [0], leafColumns: [1, 2]).Flatten(), schema);
        List<string> lazyRows = Fingerprints(GeneralizedHashTrie.Build(schema, batches, trieColumns: [0], leafColumns: [1, 2], FreeJoinTrieBuild.Lazy).Flatten(), schema);

        Assert.HasCount(3, expected);
        Assert.AreSequenceEqual(expected, eagerRows);
        Assert.AreSequenceEqual(expected, lazyRows);
    }

    [TestMethod]
    public void LazyDepthTwoBuildForcesOnlyTouchedLeaves()
    {
        IReadOnlyList<Variable> schema = ThreeColumnSchema();
        List<(uint, uint, uint)> rows = DistinctRows(2_500);
        List<SolutionBatch> batches = BuildBatches(rows, schema);

        GeneralizedHashTrie eager = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0, 1], leafColumns: [2]);
        GeneralizedHashTrie lazy = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0, 1], leafColumns: [2], FreeJoinTrieBuild.Lazy);

        //Untouched, only the unforced root entry exists.
        Assert.AreEqual(1, lazy.NodeCount);
        Assert.AreEqual(0, lazy.LeafCount);

        //The root force groups every row, so it discovers every level-1 entry —
        //the node-entry count matches the eager build's — while no leaf exists
        //until a deepest-level node forces.
        OpenAddressedTable<int> root = lazy.NodeAt(0);
        Assert.AreEqual(eager.NodeCount, lazy.NodeCount);
        Assert.AreEqual(0, lazy.LeafCount);

        //Forcing one level-1 node creates only that node's leaves.
        OpenAddressedTable<int>.Enumerator scan = root.GetEnumerator();
        Assert.IsTrue(scan.MoveNext());
        lazy.NodeAt(scan.Current.Value);
        Assert.IsGreaterThan(0, lazy.LeafCount);
        Assert.IsLessThan(eager.LeafCount, lazy.LeafCount);

        //A flatten after the partial descent still reproduces the relation
        //whole — forcing is idempotent and completes on demand.
        Assert.AreSequenceEqual(Fingerprints(batches, schema), Fingerprints(lazy.Flatten(), schema));
        Assert.AreEqual(eager.LeafCount, lazy.LeafCount);
    }

    [TestMethod]
    public void LazyFullDepthRootForceLeavesDeeperLevelsUnforced()
    {
        IReadOnlyList<Variable> schema = ThreeColumnSchema();
        List<(uint, uint, uint)> rows = DistinctRows(2_500);
        List<SolutionBatch> batches = BuildBatches(rows, schema);

        GeneralizedHashTrie eager = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0, 1, 2], leafColumns: []);
        GeneralizedHashTrie lazy = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0, 1, 2], leafColumns: [], FreeJoinTrieBuild.Lazy);

        //At depth three the root force discovers only the level-1 entries; the
        //level-2 nodes beneath them stay undiscovered until their parents
        //force, so the entry count sits strictly below the eager build's.
        Assert.AreEqual(1, lazy.NodeCount);
        lazy.NodeAt(0);
        Assert.IsLessThan(eager.NodeCount, lazy.NodeCount);

        Assert.AreSequenceEqual(Fingerprints(batches, schema), Fingerprints(lazy.Flatten(), schema));
        Assert.AreEqual(eager.NodeCount, lazy.NodeCount);
    }

    [TestMethod]
    public void EagerTrieCountsEveryNodeMapEntry()
    {
        IReadOnlyList<Variable> schema = TwoColumnSchema();
        List<SolutionBatch> batches = NodeEntryFixture(schema);

        //Full depth: the root map holds the two distinct first-column values and
        //each of the two level-1 maps holds its own two distinct second-column
        //values, so the materialised entries number two plus two plus two. The
        //three maps themselves are what NodeCount reports; this is the entry sum.
        GeneralizedHashTrie full = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0, 1], leafColumns: []);
        Assert.AreEqual(3, full.NodeCount);
        Assert.AreEqual(6L, full.RetainedNodeEntryCount);

        //Depth one: only the root map exists, carrying its two entries.
        GeneralizedHashTrie depthOne = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0], leafColumns: [1]);
        Assert.AreEqual(1, depthOne.NodeCount);
        Assert.AreEqual(2L, depthOne.RetainedNodeEntryCount);
    }

    [TestMethod]
    public void ReadingTheNodeEntryCountNeverForcesALazyTrie()
    {
        IReadOnlyList<Variable> schema = TwoColumnSchema();
        List<SolutionBatch> batches = NodeEntryFixture(schema);

        GeneralizedHashTrie lazy = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0, 1], leafColumns: [], FreeJoinTrieBuild.Lazy);

        //The root entry exists unforced, so no map holds an entry yet; reading
        //the count leaves the trie exactly as it found it.
        Assert.AreEqual(0L, lazy.RetainedNodeEntryCount);
        Assert.AreEqual(1, lazy.NodeCount);
        Assert.AreEqual(0, lazy.LeafCount);

        //Forcing the root groups every row on the first trie column, so the
        //count becomes the root map's entry count exactly — the deeper maps it
        //discovered are born unforced and contribute nothing.
        OpenAddressedTable<int> root = lazy.NodeAt(0);
        Assert.AreEqual(2, root.Count);
        Assert.AreEqual(2L, lazy.RetainedNodeEntryCount);
        Assert.AreEqual(3, lazy.NodeCount);
    }

    [TestMethod]
    public void RetainedValueCountIsTheValueSideOnly()
    {
        IReadOnlyList<Variable> schema = TwoColumnSchema();
        List<SolutionBatch> batches = NodeEntryFixture(schema);

        //A full-depth eager trie has no leaf columns, so the value side is empty
        //however many node entries the trie materialised.
        GeneralizedHashTrie full = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0, 1], leafColumns: []);
        Assert.AreEqual(0L, full.RetainedValueCount);
        Assert.AreEqual(6L, full.RetainedNodeEntryCount);

        //One hash level puts the second column in the leaves, so the value side
        //counts one packed value per row of the relation.
        GeneralizedHashTrie depthOne = GeneralizedHashTrie.Build(schema, batches, trieColumns: [0], leafColumns: [1]);
        Assert.AreEqual(6L, depthOne.RetainedValueCount);
        Assert.AreEqual(2L, depthOne.RetainedNodeEntryCount);
    }
}
