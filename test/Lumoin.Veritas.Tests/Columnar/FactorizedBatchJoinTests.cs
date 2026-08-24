using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Memory;

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

    /// <summary>The <c>?s p3 ?o3</c> predicate — the third arm of the star, and the <c>?o p3 ?t</c> branch-join probe.</summary>
    private const uint P3 = 30;

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

    /// <summary>A three-arm star fixture: each subject carries many <c>p1</c>, <c>p2</c>, AND <c>p3</c> objects, so a join over the shared subject fans out cubically.</summary>
    /// <param name="subjects">The distinct subject count.</param>
    /// <param name="fanP1">The <c>p1</c> object count per subject.</param>
    /// <param name="fanP2">The <c>p2</c> object count per subject.</param>
    /// <param name="fanP3">The <c>p3</c> object count per subject.</param>
    /// <returns>The fixture triples.</returns>
    private static List<EncodedTriple> StarFixture(int subjects, int fanP1, int fanP2, int fanP3)
    {
        const uint subjectBase = 1_000;
        const uint object1Base = 2_000;
        const uint object2Base = 3_000;
        const uint object3Base = 4_000;

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

            for(int oi = 0; oi < fanP3; oi++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P3, object3Base + (uint)((si * 100) + oi)));
            }
        }

        return triples;
    }

    /// <summary>The three-arm star query <c>?s p1 ?o . ?s p2 ?o2 . ?s p3 ?o3</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "o3"),
            ],
            registry);
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

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch factorized = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), buildSchema, ColumnarBatchScan.Scan(index, p2), probeSchema, arena);
        List<string> factorizedRows = Fingerprints(factorized.Flatten(), factorized.Schema);

        Assert.IsGreaterThan(0, flatRows.Count);
        Assert.AreSequenceEqual(flatRows, factorizedRows);

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

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch factorized = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, boundBuild), buildSchema, ColumnarBatchScan.Scan(index, probe), probeSchema, arena);

        //Branch zero (the build side) has no columns; its rows are empty
        //tuples carrying only a multiplicity of one per matched key.
        Assert.IsEmpty(factorized.BranchColumns[0]);
        foreach(FactorizedGroup group in factorized.Groups)
        {
            Assert.AreEqual(1, group.Branches.RowCountOf(0));
        }

        List<string> factorizedRows = Fingerprints(factorized.Flatten(), factorized.Schema);

        Assert.IsGreaterThan(0, flatRows.Count);
        Assert.AreSequenceEqual(flatRows, factorizedRows);
    }

    [TestMethod]
    public void StarChainAddsABranchPerArmAndFlattensToTheFlatPipeline()
    {
        List<EncodedTriple> fixture = StarFixture(subjects: 12, fanP1: 6, fanP2: 6, fanP3: 6);
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);

        VariableRegistry registry = new();
        TriplePattern p1 = EdgePattern(registry, P1, "s", "o");
        TriplePattern p2 = EdgePattern(registry, P2, "s", "o2");
        TriplePattern p3 = EdgePattern(registry, P3, "s", "o3");
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, p1);
        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, p2);
        IReadOnlyList<Variable> schema3 = ColumnarBatchScan.ScanSchemaOf(index, p3);

        //Two arms factorise on the shared subject, then the third attaches as
        //a new branch on the same key — no flat product between the joins.
        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), schema1, ColumnarBatchScan.Scan(index, p2), schema2, arena);
        FactorizedBatch? star = FactorizedBatchJoin.AddBranch(firstTwo, ColumnarBatchScan.Scan(index, p3), schema3, arena);

        Assert.IsNotNull(star);
        Assert.HasCount(3, star.BranchColumns);

        //The flat pipeline over the same star is the differential oracle.
        ColumnarBatchPlan plan = ColumnarBatchPipeline.TryPlan(index, StarQuery(new VariableRegistry()))!;
        List<string> flatRows = Fingerprints(ColumnarBatchPipeline.Run(index, plan, VeritasMemoryPool<uint>.Shared), plan.Schema);
        List<string> factorizedRows = Fingerprints(star.Flatten(), star.Schema);

        Assert.IsGreaterThan(0, flatRows.Count);
        Assert.AreSequenceEqual(flatRows, factorizedRows);

        //12 subjects × (6 + 6 + 6) stored tuples versus 12 × 6³ flat rows.
        Assert.AreEqual(flatRows.Count, (int)star.FlatRowCount);
        Assert.AreEqual(12L * 6 * 6 * 6, star.FlatRowCount);
        Assert.AreEqual(12L * (6 + 6 + 6), star.FactorizedTupleCount);
        Assert.IsLessThan(star.FlatRowCount, star.FactorizedTupleCount);
    }

    [TestMethod]
    public void AddBranchRefusesAProbeThatJoinsOnABranchVariable()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 5, fanP1: 3, fanP2: 3, fanP3: 2));
        VariableRegistry registry = new();
        TriplePattern p1 = EdgePattern(registry, P1, "s", "o");
        TriplePattern p2 = EdgePattern(registry, P2, "s", "o2");

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, p1), ColumnarBatchScan.ScanSchemaOf(index, p1),
            ColumnarBatchScan.Scan(index, p2), ColumnarBatchScan.ScanSchemaOf(index, p2), arena);

        //?s p3 ?o shares the key ?s but also the branch variable ?o — a non-key
        //join the single-level factorisation cannot extend, so it must refuse.
        TriplePattern branchProbe = EdgePattern(registry, P3, "s", "o");
        FactorizedBatch? refused = FactorizedBatchJoin.AddBranch(
            firstTwo, ColumnarBatchScan.Scan(index, branchProbe), ColumnarBatchScan.ScanSchemaOf(index, branchProbe), arena);

        Assert.IsNull(refused);
    }

    /// <summary>A chain fixture <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c>: each hub fans in from <paramref name="fanA"/> subjects, fans out to <paramref name="fanB"/> objects, and each of those carries <paramref name="fanC"/> further objects.</summary>
    /// <param name="hubs">The distinct <c>?x</c> hub count.</param>
    /// <param name="fanA">The <c>?a</c> count per hub.</param>
    /// <param name="fanB">The <c>?b</c> count per hub.</param>
    /// <param name="fanC">The <c>?c</c> count per <c>?b</c>.</param>
    /// <returns>The fixture triples.</returns>
    private static List<EncodedTriple> ChainFanFixture(int hubs, int fanA, int fanB, int fanC)
    {
        const uint xBase = 1_000;
        const uint aBase = 2_000;
        const uint bBase = 3_000;
        const uint cBase = 4_000;

        List<EncodedTriple> triples = [];
        for(int xi = 0; xi < hubs; xi++)
        {
            uint x = xBase + (uint)xi;
            for(int ai = 0; ai < fanA; ai++)
            {
                triples.Add(EncodedTriple.FromEncoded(aBase + (uint)((xi * 100) + ai), P1, x));
            }

            for(int bi = 0; bi < fanB; bi++)
            {
                uint b = bBase + (uint)((xi * 100) + bi);
                triples.Add(EncodedTriple.FromEncoded(x, P2, b));

                for(int ci = 0; ci < fanC; ci++)
                {
                    triples.Add(EncodedTriple.FromEncoded(b, P3, cBase + (uint)((((xi * 100) + bi) * 100) + ci)));
                }
            }
        }

        return triples;
    }

    /// <summary>The chain query <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c> over a fresh registry, for the flat oracle plan.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "a", "x"),
                EdgePattern(registry, P2, "x", "b"),
                EdgePattern(registry, P3, "b", "c"),
            ],
            registry);
    }

    [TestMethod]
    public void NestedChainFlattensToTheFlatPipelineAndCompresses()
    {
        const int hubs = 8;
        const int fanA = 4;
        const int fanB = 4;
        const int fanC = 4;
        List<EncodedTriple> fixture = ChainFanFixture(hubs, fanA, fanB, fanC);
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);

        VariableRegistry registry = new();
        TriplePattern r1 = EdgePattern(registry, P1, "a", "x");
        TriplePattern r2 = EdgePattern(registry, P2, "x", "b");
        TriplePattern r3 = EdgePattern(registry, P3, "b", "c");
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, r1);
        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, r2);
        IReadOnlyList<Variable> schema3 = ColumnarBatchScan.ScanSchemaOf(index, r3);

        //Join the first two arms on ?x, then nest the ?b branch under ?b to
        //attach the ?b p3 ?c arm — a second factorisation level.
        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, r1), schema1, ColumnarBatchScan.Scan(index, r2), schema2, arena);
        FactorizedBatch? chain = FactorizedBatchJoin.NestBranch(firstTwo, registry.GetOrAdd("b"), ColumnarBatchScan.Scan(index, r3), schema3, arena);

        Assert.IsNotNull(chain);
        Assert.IsTrue(chain.HasNestedBranches);

        //The flat streamed pipeline over the same chain is the differential oracle.
        ColumnarBatchPlan plan = ColumnarBatchPipeline.TryPlan(index, ChainQuery(new VariableRegistry()), useSemijoinReduction: false, useFactorizedStar: false)!;
        List<string> flatRows = Fingerprints(ColumnarBatchPipeline.Run(index, plan, VeritasMemoryPool<uint>.Shared), plan.Schema);
        List<string> factorizedRows = Fingerprints(chain.Flatten(), chain.Schema);

        Assert.IsGreaterThan(0, flatRows.Count);
        Assert.AreSequenceEqual(flatRows, factorizedRows);

        //Flat = hubs·fanA·fanB·fanC; stored = hubs·(fanA + fanB·fanC): the ?a
        //arm independent of the (?b,?c) sub-tree given ?x.
        Assert.AreEqual(flatRows.Count, (int)chain.FlatRowCount);
        Assert.AreEqual((long)hubs * fanA * fanB * fanC, chain.FlatRowCount);
        Assert.AreEqual((long)hubs * (fanA + (fanB * fanC)), chain.FactorizedTupleCount);
        Assert.IsLessThan(chain.FlatRowCount, chain.FactorizedTupleCount);
    }

    [TestMethod]
    public void NestBranchRefusesTheKeyVariableAndAlreadyNestedInput()
    {
        List<EncodedTriple> fixture = ChainFanFixture(hubs: 4, fanA: 2, fanB: 2, fanC: 2);
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);

        VariableRegistry registry = new();
        TriplePattern r1 = EdgePattern(registry, P1, "a", "x");
        TriplePattern r2 = EdgePattern(registry, P2, "x", "b");
        TriplePattern r3 = EdgePattern(registry, P3, "b", "c");
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, r1);
        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, r2);
        IReadOnlyList<Variable> schema3 = ColumnarBatchScan.ScanSchemaOf(index, r3);

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, r1), schema1, ColumnarBatchScan.Scan(index, r2), schema2, arena);

        //?x is the group key, not a branch — nesting cannot target it.
        Assert.IsNull(FactorizedBatchJoin.NestBranch(firstTwo, registry.GetOrAdd("x"), ColumnarBatchScan.Scan(index, r2), schema2, arena));

        //Once nested, a second nesting would exceed depth two and is refused.
        FactorizedBatch chain = FactorizedBatchJoin.NestBranch(firstTwo, registry.GetOrAdd("b"), ColumnarBatchScan.Scan(index, r3), schema3, arena)!;
        TriplePattern r4 = EdgePattern(registry, P1, "c", "d");
        Assert.IsNull(FactorizedBatchJoin.NestBranch(chain, registry.GetOrAdd("c"), ColumnarBatchScan.Scan(index, r4), ColumnarBatchScan.ScanSchemaOf(index, r4), arena));
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
        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedGroup group = new(
            keyValues: arena.AllocateFrom([7]),
            branches: FactorizedBranches.Of([[100, 101], [200, 201]], [2, 2], [1, 1], arena));
        FactorizedBatch batch = new([key, left, right], keyColumns: [0], branchColumns: [[1], [2]], groups: [group]);

        Assert.AreEqual(4, batch.FlatRowCount);
        Assert.AreEqual(4, batch.FactorizedTupleCount);

        List<string> rows = Fingerprints(batch.Flatten(), batch.Schema);
        List<string> expected = Fingerprints(
            ManualBatch(batch.Schema, [[7, 100, 200], [7, 100, 201], [7, 101, 200], [7, 101, 201]]), batch.Schema);

        Assert.AreSequenceEqual(expected, rows);
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
