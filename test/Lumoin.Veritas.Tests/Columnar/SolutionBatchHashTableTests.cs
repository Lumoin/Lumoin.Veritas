using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The chained hash table's contract: every build row matching a
/// key is reachable by chain walk exactly once, single- and
/// two-column keys both index correctly, and an absent key yields
/// no match — the allocation-lean structure behind the batched
/// join.
/// </summary>
[TestClass]
internal sealed class SolutionBatchHashTableTests
{
    /// <summary>Builds a one-batch stream over the given two-column rows.</summary>
    /// <param name="schema">The batch schema.</param>
    /// <param name="rows">The rows as (column0, column1) pairs.</param>
    /// <returns>The single-batch stream.</returns>
    private static List<SolutionBatch> OneBatch(IReadOnlyList<Variable> schema, params (uint Column0, uint Column1)[] rows)
    {
        SolutionBatch batch = new(schema);
        for(int row = 0; row < rows.Length; row++)
        {
            batch.ColumnSpan(0)[row] = rows[row].Column0;
            batch.ColumnSpan(1)[row] = rows[row].Column1;
        }

        batch.SetCount(rows.Length);

        return [batch];
    }

    /// <summary>Collects every build row matching a key, by chain walk.</summary>
    /// <param name="table">The table.</param>
    /// <param name="key">The packed key.</param>
    /// <returns>The matching row ids in chain order.</returns>
    private static List<int> MatchesOf(SolutionBatchHashTable table, JoinKey key)
    {
        List<int> matches = [];
        for(int rowId = table.FirstMatch(key); rowId >= 0; rowId = table.NextMatch(rowId))
        {
            matches.Add(rowId);
        }

        return matches;
    }

    [TestMethod]
    public void SingleColumnKeyIndexesEveryMatchingRow()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");

        //Three rows share key 7 in column 0; one row has key 9.
        SolutionBatchHashTable table = SolutionBatchHashTable.Build(
            OneBatch([a, b], (7, 100), (9, 200), (7, 101), (7, 102)),
            columnCount: 2,
            keyColumn0: 0,
            keyColumn1: -1);

        Assert.AreEqual(4, table.RowCount);
        Assert.AreEqual(2, table.ColumnCount);

        List<int> sevens = MatchesOf(table, JoinKey.Pack(7, 0));
        Assert.HasCount(3, sevens);

        //Every matched row's column-0 value is 7; column-1 values
        //cover exactly the three sevens, each once.
        HashSet<uint> columnOneValues = [];
        foreach(int rowId in sevens)
        {
            Assert.AreEqual(7u, table.ValueAt(0, rowId));
            Assert.IsTrue(columnOneValues.Add(table.ValueAt(1, rowId)));
        }

        Assert.AreSequenceEqual(new uint[] { 100, 101, 102 }, new List<uint>(columnOneValues), SequenceOrder.InAnyOrder);

        Assert.HasCount(1, MatchesOf(table, JoinKey.Pack(9, 0)));
        Assert.HasCount(0, MatchesOf(table, JoinKey.Pack(42, 0)));
    }

    [TestMethod]
    public void TwoColumnKeyDistinguishesPairs()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");

        //(1,2) appears twice; (1,3) once — same first column, distinct keys.
        SolutionBatchHashTable table = SolutionBatchHashTable.Build(
            OneBatch([a, b], (1, 2), (1, 3), (1, 2)),
            columnCount: 2,
            keyColumn0: 0,
            keyColumn1: 1);

        Assert.HasCount(2, MatchesOf(table, JoinKey.Pack(1, 2)));
        Assert.HasCount(1, MatchesOf(table, JoinKey.Pack(1, 3)));
        Assert.HasCount(0, MatchesOf(table, JoinKey.Pack(1, 4)));
    }

    [TestMethod]
    public void MatchesSpanMultipleBuildBatches()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");

        List<SolutionBatch> first = OneBatch([a, b], (5, 1), (5, 2));
        List<SolutionBatch> second = OneBatch([a, b], (5, 3), (6, 4));
        List<SolutionBatch> stream = [.. first, .. second];

        SolutionBatchHashTable table = SolutionBatchHashTable.Build(stream, columnCount: 2, keyColumn0: 0, keyColumn1: -1);

        Assert.AreEqual(4, table.RowCount);
        Assert.HasCount(3, MatchesOf(table, JoinKey.Pack(5, 0)));
        Assert.HasCount(1, MatchesOf(table, JoinKey.Pack(6, 0)));
    }
}
