using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The open-addressed join table's contract: it answers exactly what the
/// chained <see cref="SolutionBatchHashTable"/> does. For any build set and any
/// probe key the matching rows — walked through FirstMatch/NextMatch and read
/// through ValueAt — are the same multiset, so the open-addressed table can
/// stand in for the chained one behind the join. The chained table is the
/// differential oracle.
/// </summary>
[TestClass]
internal sealed class OpenAddressedBatchHashTableTests
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

    /// <summary>A two-column schema for the build rows.</summary>
    /// <returns>The schema.</returns>
    private static IReadOnlyList<Variable> TwoColumnSchema()
    {
        VariableRegistry registry = new();

        return [registry.GetOrAdd("c0"), registry.GetOrAdd("c1")];
    }

    /// <summary>Packs the two-column rows into full <see cref="SolutionBatch"/>es of at most <see cref="SolutionBatch.BatchLength"/> rows.</summary>
    /// <param name="rows">The rows, each a pair of column values.</param>
    /// <param name="schema">The two-column schema.</param>
    /// <returns>The batch stream.</returns>
    private static List<SolutionBatch> BuildBatches(List<(uint Column0, uint Column1)> rows, IReadOnlyList<Variable> schema)
    {
        List<SolutionBatch> batches = [];
        SolutionBatch batch = new(schema);
        int filled = 0;

        foreach((uint column0, uint column1) in rows)
        {
            batch.ColumnSpan(0)[filled] = column0;
            batch.ColumnSpan(1)[filled] = column1;
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

    /// <summary>Walks one key's chain through the given table accessors, collecting the matched rows' full column tuples as sorted fingerprints.</summary>
    /// <param name="first">The table's FirstMatch.</param>
    /// <param name="nextMatch">The table's NextMatch.</param>
    /// <param name="valueAt">The table's ValueAt.</param>
    /// <param name="columnCount">The table's column count.</param>
    /// <param name="key">The probe key.</param>
    /// <returns>The matched rows' fingerprints, sorted.</returns>
    private static List<string> Collect(
        Func<JoinKey, int> first,
        Func<int, int> nextMatch,
        Func<int, int, uint> valueAt,
        int columnCount,
        JoinKey key)
    {
        List<string> rows = [];
        for(int rowId = first(key); rowId >= 0; rowId = nextMatch(rowId))
        {
            List<string> cells = [];
            for(int column = 0; column < columnCount; column++)
            {
                cells.Add(valueAt(column, rowId).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            rows.Add(string.Join(",", cells));
        }

        rows.Sort(StringComparer.Ordinal);

        return rows;
    }

    /// <summary>Asserts the two tables answer every probe key identically, over keys both present and absent.</summary>
    /// <param name="rows">The build rows.</param>
    /// <param name="keyColumn1">The second key column, or −1 for a single-variable key.</param>
    /// <param name="probeKeys">The keys to probe.</param>
    private static void AssertTablesAgree(List<(uint Column0, uint Column1)> rows, int keyColumn1, IEnumerable<JoinKey> probeKeys)
    {
        IReadOnlyList<Variable> schema = TwoColumnSchema();
        List<SolutionBatch> batches = BuildBatches(rows, schema);

        SolutionBatchHashTable chained = SolutionBatchHashTable.Build(batches, 2, 0, keyColumn1);
        OpenAddressedBatchHashTable open = OpenAddressedBatchHashTable.Build(batches, 2, 0, keyColumn1);

        Assert.AreEqual(chained.RowCount, open.RowCount);

        foreach(JoinKey key in probeKeys)
        {
            List<string> viaChained = Collect(chained.FirstMatch, chained.NextMatch, chained.ValueAt, chained.ColumnCount, key);
            List<string> viaOpen = Collect(open.FirstMatch, open.NextMatch, open.ValueAt, open.ColumnCount, key);

            Assert.AreSequenceEqual(viaChained, viaOpen, $"key {key.Value} disagreed");
        }
    }

    [TestMethod]
    public void AgreesWithTheChainedTableOnASingleVariableKeyWithFanOutAndGrowth()
    {
        //Many rows over a few hundred keys, forcing several grows and deep
        //chains; key column 0, column 1 a distinct payload per row.
        List<(uint, uint)> rows = [];
        List<JoinKey> probeKeys = [];
        ulong state = 7;
        for(uint i = 0; i < 6_000; i++)
        {
            state = Mix(state);
            uint key = 100 + (uint)(state % 400);
            rows.Add((key, i));
        }

        for(uint key = 80; key < 540; key++)
        {
            probeKeys.Add(JoinKey.Pack(key, 0));
        }

        AssertTablesAgree(rows, keyColumn1: -1, probeKeys);
    }

    [TestMethod]
    public void AgreesWithTheChainedTableOnATwoVariableKey()
    {
        List<(uint, uint)> rows = [];
        List<JoinKey> probeKeys = [];
        ulong state = 19;
        for(uint i = 0; i < 4_000; i++)
        {
            state = Mix(state);
            uint key0 = 10 + (uint)(state % 60);
            uint key1 = 200 + (uint)((state >> 16) % 60);
            rows.Add((key0, key1));
        }

        for(uint key0 = 5; key0 < 75; key0++)
        {
            for(uint key1 = 195; key1 < 265; key1 += 7)
            {
                probeKeys.Add(JoinKey.Pack(key0, key1));
            }
        }

        AssertTablesAgree(rows, keyColumn1: 1, probeKeys);
    }

    [TestMethod]
    public void HandlesTheZeroKeyWhichMarksAnEmptySlot()
    {
        //Key 0 is a valid join key; the table must not confuse it with an
        //empty slot. Include rows carrying key 0 and probe for it.
        List<(uint, uint)> rows =
        [
            (0, 11),
            (0, 12),
            (5, 13),
            (0, 14),
        ];

        AssertTablesAgree(rows, keyColumn1: -1, [JoinKey.Pack(0, 0), JoinKey.Pack(5, 0), JoinKey.Pack(9, 0)]);
    }
}
