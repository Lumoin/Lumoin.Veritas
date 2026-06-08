using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The generalized hash trie's lossless contract: for any trie/leaf column
/// split and any column order, flattening the built trie reproduces the input
/// relation exactly. The flatten is the differential oracle the Free Join
/// generic join will be built on, so the trie must lose nothing — across a
/// full-depth trie (no leaf columns), a binary-join-shaped depth-1 trie (most
/// columns in the leaf), and reordered trie levels.
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

    /// <summary>Builds a trie over the given split and asserts its flatten reproduces the input relation.</summary>
    /// <param name="trieColumns">The trie-level column indices.</param>
    /// <param name="leafColumns">The leaf column indices.</param>
    private static void AssertLosslessSplit(int[] trieColumns, int[] leafColumns)
    {
        IReadOnlyList<Variable> schema = ThreeColumnSchema();
        List<(uint, uint, uint)> rows = DistinctRows(2_500);
        List<SolutionBatch> batches = BuildBatches(rows, schema);

        List<string> expected = Fingerprints(batches, schema);

        GeneralizedHashTrie trie = GeneralizedHashTrie.Build(schema, batches, trieColumns, leafColumns);
        List<string> flattened = Fingerprints(trie.Flatten(), schema);

        Assert.IsGreaterThan(0, expected.Count);
        CollectionAssert.AreEqual(expected, flattened);
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
}
