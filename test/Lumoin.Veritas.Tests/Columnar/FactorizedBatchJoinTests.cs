using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The factorising join's contract: it produces the same answers as the
/// flat batched hash join — flattening the factorised result reproduces the
/// flat product row-for-row — while storing the sum of the per-key branch
/// sizes where the flat join would materialise their product. The flatten
/// bridge is the differential oracle; the tuple-count assertions pin the
/// compression the representation exists for.
/// </summary>
[TestClass]
internal sealed class FactorizedBatchJoinTests
{
    /// <summary>The <c>?s p1 ?o</c> predicate.</summary>
    private const uint P1 = 10;

    /// <summary>The <c>?s p2 ?o2</c> predicate.</summary>
    private const uint P2 = 20;

    /// <summary>A fan-out fixture: each subject carries many <c>p1</c> objects AND many <c>p2</c> objects, so the join over the shared subject blows up multiplicatively.</summary>
    /// <param name="subjects">The distinct subject count.</param>
    /// <param name="fanP1">The <c>p1</c> object count per subject.</param>
    /// <param name="fanP2">The <c>p2</c> object count per subject.</param>
    /// <returns>The fixture triples.</returns>
    private static List<EncodedTriple> FanOutFixture(int subjects, int fanP1, int fanP2)
    {
        const uint subjectBase = 1_000;
        const uint object1Base = 2_000;
        const uint object2Base = 3_000;

        List<EncodedTriple> triples = [];
        for(int si = 0; si < subjects; si++)
        {
            uint subject = subjectBase + (uint)si;
            for(int oi = 0; oi < fanP1; oi++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, object1Base + (uint)((si * 100) + oi)));
            }

            for(int oi = 0; oi < fanP2; oi++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P2, object2Base + (uint)((si * 100) + oi)));
            }
        }

        return triples;
    }

    /// <summary>The pattern <c>?subject {predicate} ?object</c> over fresh variables.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <param name="predicate">The bound predicate.</param>
    /// <param name="subjectName">The subject variable name.</param>
    /// <param name="objectName">The object variable name.</param>
    /// <returns>The pattern.</returns>
    private static TriplePattern EdgePattern(VariableRegistry registry, uint predicate, string subjectName, string objectName)
    {
        return new TriplePattern(
            PatternPosition.OfVariable(registry.GetOrAdd(subjectName)),
            PatternPosition.Bound(TermId.FromEncoded(predicate)),
            PatternPosition.OfVariable(registry.GetOrAdd(objectName)));
    }

    /// <summary>Drains a batch stream into order-insensitive per-row fingerprints over the batch schema.</summary>
    /// <param name="batches">The batch stream.</param>
    /// <param name="schema">The stream's schema, positional against its columns.</param>
    /// <returns>The sorted fingerprints — one per flat row.</returns>
    private static List<string> Fingerprints(IEnumerable<SolutionBatch> batches, IReadOnlyList<Variable> schema)
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

    [TestMethod]
    public void FlattenedFactorizedJoinEqualsFlatHashJoinAndStoresFewerTuples()
    {
        List<EncodedTriple> fixture = FanOutFixture(subjects: 20, fanP1: 15, fanP2: 15);
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);

        VariableRegistry registry = new();
        TriplePattern p1 = EdgePattern(registry, P1, "s", "o");
        TriplePattern p2 = EdgePattern(registry, P2, "s", "o2");

        IReadOnlyList<Variable> buildSchema = ColumnarBatchScan.ScanSchemaOf(index, p1);
        IReadOnlyList<Variable> probeSchema = ColumnarBatchScan.ScanSchemaOf(index, p2);

        IEnumerable<SolutionBatch> flat = SolutionBatchJoin.HashJoin(
            ColumnarBatchScan.Scan(index, p1), buildSchema, ColumnarBatchScan.Scan(index, p2), probeSchema);
        List<string> flatRows = Fingerprints(flat, [.. buildSchema, .. probeSchema.Where(v => !buildSchema.Contains(v))]);

        FactorizedBatch factorized = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), buildSchema, ColumnarBatchScan.Scan(index, p2), probeSchema);
        List<string> factorizedRows = Fingerprints(factorized.Flatten(), factorized.Schema);

        Assert.IsGreaterThan(0, flatRows.Count);
        CollectionAssert.AreEqual(flatRows, factorizedRows);

        //The headline: the flattened cardinality is preserved, but the stored
        //footprint is the per-key SUM of the branches, not their PRODUCT.
        Assert.AreEqual(flatRows.Count, (int)factorized.FlatRowCount);
        Assert.IsLessThan(factorized.FlatRowCount, factorized.FactorizedTupleCount);

        //20 subjects × (15 + 15) stored tuples versus 20 × 15 × 15 flat rows.
        Assert.AreEqual(20L * (15 + 15), factorized.FactorizedTupleCount);
        Assert.AreEqual(20L * 15 * 15, factorized.FlatRowCount);
    }

    [TestMethod]
    public void JoinWithAFullyBoundBuildSideKeepsAnEmptyBranchAndStillAgrees()
    {
        List<EncodedTriple> fixture = FanOutFixture(subjects: 8, fanP1: 1, fanP2: 6);

        //Bind the p1 object so the build side scans to the subject alone: its
        //schema equals the join key, leaving branch zero with no columns.
        fixture.Add(EncodedTriple.FromEncoded(1_000, P1, 2_000));
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);

        VariableRegistry registry = new();
        TriplePattern boundBuild = new(
            PatternPosition.OfVariable(registry.GetOrAdd("s")),
            PatternPosition.Bound(TermId.FromEncoded(P1)),
            PatternPosition.Bound(TermId.FromEncoded(2_000)));
        TriplePattern probe = EdgePattern(registry, P2, "s", "o2");

        IReadOnlyList<Variable> buildSchema = ColumnarBatchScan.ScanSchemaOf(index, boundBuild);
        IReadOnlyList<Variable> probeSchema = ColumnarBatchScan.ScanSchemaOf(index, probe);

        Assert.HasCount(1, buildSchema);

        IEnumerable<SolutionBatch> flat = SolutionBatchJoin.HashJoin(
            ColumnarBatchScan.Scan(index, boundBuild), buildSchema, ColumnarBatchScan.Scan(index, probe), probeSchema);
        List<string> flatRows = Fingerprints(flat, [.. buildSchema, .. probeSchema.Where(v => !buildSchema.Contains(v))]);

        FactorizedBatch factorized = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, boundBuild), buildSchema, ColumnarBatchScan.Scan(index, probe), probeSchema);

        //Branch zero (the build side) has no columns; its rows are empty
        //tuples carrying only a multiplicity of one per matched key.
        Assert.IsEmpty(factorized.BranchColumns[0]);
        foreach(FactorizedGroup group in factorized.Groups)
        {
            Assert.AreEqual(1, group.BranchRowCounts[0]);
        }

        List<string> factorizedRows = Fingerprints(factorized.Flatten(), factorized.Schema);

        Assert.IsGreaterThan(0, flatRows.Count);
        CollectionAssert.AreEqual(flatRows, factorizedRows);
    }

    [TestMethod]
    public void FlattenWalksTheCrossProductOfEveryBranch()
    {
        VariableRegistry registry = new();
        Variable key = registry.GetOrAdd("k");
        Variable left = registry.GetOrAdd("l");
        Variable right = registry.GetOrAdd("r");

        //One group, key column 0, two single-column branches with two rows
        //each: the flatten must walk all four combinations.
        FactorizedGroup group = new(
            keyValues: [7],
            branchValues: [[100, 101], [200, 201]],
            branchRowCounts: [2, 2]);
        FactorizedBatch batch = new([key, left, right], keyColumns: [0], branchColumns: [[1], [2]], groups: [group]);

        Assert.AreEqual(4, batch.FlatRowCount);
        Assert.AreEqual(4, batch.FactorizedTupleCount);

        List<string> rows = Fingerprints(batch.Flatten(), batch.Schema);
        List<string> expected = Fingerprints(
            ManualBatch(batch.Schema, [[7, 100, 200], [7, 100, 201], [7, 101, 200], [7, 101, 201]]), batch.Schema);

        CollectionAssert.AreEqual(expected, rows);
    }

    /// <summary>A single batch holding the given rows, for asserting an expected flat product.</summary>
    /// <param name="schema">The batch schema.</param>
    /// <param name="rows">The rows, each a value per schema column.</param>
    /// <returns>The single-batch stream.</returns>
    private static IEnumerable<SolutionBatch> ManualBatch(IReadOnlyList<Variable> schema, uint[][] rows)
    {
        SolutionBatch batch = new(schema);
        for(int row = 0; row < rows.Length; row++)
        {
            for(int column = 0; column < schema.Count; column++)
            {
                batch.ColumnSpan(column)[row] = rows[row][column];
            }
        }

        batch.SetCount(rows.Length);

        return [batch];
    }
}
