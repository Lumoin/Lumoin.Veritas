using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The columnar semijoin's contract: a reduced target holds exactly
/// the rows a brute-force shared-key membership keeps, never grows
/// the target, and leaves the downstream join's answer unchanged —
/// the invariant Yannakakis relies on to strip dangling tuples
/// without affecting the result.
/// </summary>
[TestClass]
internal sealed class SolutionBatchSemijoinTests
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

    /// <summary>A fixture whose subject keys partly overlap so a semijoin both keeps and drops rows.</summary>
    /// <returns>The index.</returns>
    private static ColumnarTripleIndex Fixture()
    {
        List<EncodedTriple> triples = [];
        ulong state = 71;
        for(int i = 0; i < 4_000; i++)
        {
            state = Mix(state);

            //The 200-edge subjects span 100..159; the 201-edge subjects
            //span 130..189, so the shared band 130..159 survives and the
            //rest is dropped by a semijoin in either direction.
            uint left = 100 + (uint)(state % 60);
            uint right = 130 + (uint)((state >> 8) % 60);
            triples.Add(EncodedTriple.FromEncoded(left, 200, 300 + (uint)((state >> 16) % 50)));
            triples.Add(EncodedTriple.FromEncoded(right, 201, 400 + (uint)((state >> 24) % 20)));
        }

        return ColumnarTripleIndex.Build(triples);
    }

    /// <summary>Materialises a pattern's scan into a batch list.</summary>
    /// <param name="index">The index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The batches.</returns>
    private static List<SolutionBatch> Materialise(ColumnarTripleIndex index, TriplePattern pattern)
    {
        return [.. ColumnarBatchScan.Scan(index, pattern)];
    }

    /// <summary>The variable's position in the schema, or −1.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="variable">The variable.</param>
    /// <returns>The position, or −1.</returns>
    private static int IndexOf(IReadOnlyList<Variable> schema, Variable variable)
    {
        for(int i = 0; i < schema.Count; i++)
        {
            if(schema[i] == variable)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The shared-column positions of the two schemas, in target order.</summary>
    /// <param name="targetSchema">The target schema.</param>
    /// <param name="probeSchema">The probe schema.</param>
    /// <returns>The parallel target and probe column lists.</returns>
    private static (List<int> Target, List<int> Probe) SharedColumns(IReadOnlyList<Variable> targetSchema, IReadOnlyList<Variable> probeSchema)
    {
        List<int> target = [];
        List<int> probe = [];
        for(int t = 0; t < targetSchema.Count; t++)
        {
            int p = IndexOf(probeSchema, targetSchema[t]);
            if(p >= 0)
            {
                target.Add(t);
                probe.Add(p);
            }
        }

        return (target, probe);
    }

    /// <summary>A row's shared-variable key as a string.</summary>
    /// <param name="batch">The batch.</param>
    /// <param name="columns">The shared columns.</param>
    /// <param name="row">The row.</param>
    /// <returns>The key string.</returns>
    private static string KeyOf(SolutionBatch batch, List<int> columns, int row)
    {
        return string.Join(",", columns.Select(column => batch.ColumnOf(column)[row].ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>A row's full fingerprint over a schema, order-insensitive.</summary>
    /// <param name="batch">The batch.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="row">The row.</param>
    /// <returns>The fingerprint.</returns>
    private static string Fingerprint(SolutionBatch batch, IReadOnlyList<Variable> schema, int row)
    {
        List<string> parts = [];
        for(int column = 0; column < schema.Count; column++)
        {
            parts.Add($"{schema[column].Id}={batch.ColumnOf(column)[row]}");
        }

        parts.Sort(StringComparer.Ordinal);

        return string.Join(";", parts);
    }

    /// <summary>Flattens a batch list into sorted row fingerprints over its schema.</summary>
    /// <param name="batches">The batches.</param>
    /// <param name="schema">The schema.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> Flatten(IReadOnlyList<SolutionBatch> batches, IReadOnlyList<Variable> schema)
    {
        List<string> rows = [];
        foreach(SolutionBatch batch in batches)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                rows.Add(Fingerprint(batch, schema, row));
            }
        }

        rows.Sort(StringComparer.Ordinal);

        return rows;
    }

    /// <summary>The brute-force reference semijoin: target rows whose shared key occurs in the probe.</summary>
    /// <param name="target">The target batches.</param>
    /// <param name="targetSchema">The target schema.</param>
    /// <param name="probe">The probe batches.</param>
    /// <param name="probeSchema">The probe schema.</param>
    /// <returns>The kept rows as sorted fingerprints.</returns>
    private static List<string> NaiveSemijoin(
        List<SolutionBatch> target, IReadOnlyList<Variable> targetSchema,
        List<SolutionBatch> probe, IReadOnlyList<Variable> probeSchema)
    {
        (List<int> targetColumns, List<int> probeColumns) = SharedColumns(targetSchema, probeSchema);

        HashSet<string> probeKeys = [];
        foreach(SolutionBatch batch in probe)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                probeKeys.Add(KeyOf(batch, probeColumns, row));
            }
        }

        List<string> rows = [];
        foreach(SolutionBatch batch in target)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                if(probeKeys.Contains(KeyOf(batch, targetColumns, row)))
                {
                    rows.Add(Fingerprint(batch, targetSchema, row));
                }
            }
        }

        rows.Sort(StringComparer.Ordinal);

        return rows;
    }

    /// <summary>The total committed rows across a batch list.</summary>
    /// <param name="batches">The batches.</param>
    /// <returns>The row count.</returns>
    private static int RowCount(IReadOnlyList<SolutionBatch> batches)
    {
        int count = 0;
        foreach(SolutionBatch batch in batches)
        {
            count += batch.Count;
        }

        return count;
    }

    [TestMethod]
    public void ReducedTargetMatchesBruteForceAndDoesNotGrow()
    {
        ColumnarTripleIndex index = Fixture();
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");

        TriplePattern left = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o));
        TriplePattern right = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2));

        IReadOnlyList<Variable> leftSchema = ColumnarBatchScan.ScanSchemaOf(index, left);
        IReadOnlyList<Variable> rightSchema = ColumnarBatchScan.ScanSchemaOf(index, right);
        List<SolutionBatch> leftRelation = Materialise(index, left);
        List<SolutionBatch> rightRelation = Materialise(index, right);

        IReadOnlyList<SolutionBatch> reduced = SolutionBatchSemijoin.Reduce(leftRelation, leftSchema, rightRelation, rightSchema);

        List<string> expected = NaiveSemijoin(leftRelation, leftSchema, rightRelation, rightSchema);
        List<string> actual = Flatten(reduced, leftSchema);

        Assert.IsGreaterThan(0, expected.Count);
        Assert.AreSequenceEqual(expected, actual);

        //A semijoin strips dangling rows but keeps some: a strict, non-empty reduction here.
        Assert.IsLessThan(RowCount(leftRelation), RowCount(reduced));
        Assert.IsGreaterThan(0, RowCount(reduced));
    }

    [TestMethod]
    public void SemijoinDoesNotChangeTheDownstreamJoin()
    {
        ColumnarTripleIndex index = Fixture();
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");

        TriplePattern left = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o));
        TriplePattern right = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2));

        IReadOnlyList<Variable> leftSchema = ColumnarBatchScan.ScanSchemaOf(index, left);
        IReadOnlyList<Variable> rightSchema = ColumnarBatchScan.ScanSchemaOf(index, right);
        List<SolutionBatch> leftRelation = Materialise(index, left);
        List<SolutionBatch> rightRelation = Materialise(index, right);

        List<Variable> outputSchema = [.. leftSchema];
        outputSchema.AddRange(rightSchema.Where(variable => !leftSchema.Contains(variable)));

        List<string> joinOnOriginal = Flatten([.. SolutionBatchJoin.HashJoin(leftRelation, leftSchema, rightRelation, rightSchema)], outputSchema);

        IReadOnlyList<SolutionBatch> reducedLeft = SolutionBatchSemijoin.Reduce(leftRelation, leftSchema, rightRelation, rightSchema);
        List<string> joinOnReduced = Flatten([.. SolutionBatchJoin.HashJoin(reducedLeft, leftSchema, rightRelation, rightSchema)], outputSchema);

        Assert.IsGreaterThan(0, joinOnOriginal.Count);
        Assert.AreSequenceEqual(joinOnOriginal, joinOnReduced);
    }

    [TestMethod]
    public void TwoVariableSharedKeyReducesCorrectly()
    {
        ColumnarTripleIndex index = Fixture();
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        //Both patterns bind the same two variables (s, o) — a two-variable shared key.
        TriplePattern target = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o));
        TriplePattern probe = new(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o));

        IReadOnlyList<Variable> targetSchema = ColumnarBatchScan.ScanSchemaOf(index, target);
        IReadOnlyList<Variable> probeSchema = ColumnarBatchScan.ScanSchemaOf(index, probe);
        List<SolutionBatch> targetRelation = Materialise(index, target);
        List<SolutionBatch> probeRelation = Materialise(index, probe);

        Assert.HasCount(2, targetSchema);

        IReadOnlyList<SolutionBatch> reduced = SolutionBatchSemijoin.Reduce(targetRelation, targetSchema, probeRelation, probeSchema);

        //A relation semijoined against itself on all its variables is unchanged.
        List<string> expected = Flatten(targetRelation, targetSchema);
        List<string> actual = Flatten(reduced, targetSchema);
        Assert.AreSequenceEqual(expected, actual);
    }

    [TestMethod]
    public void NoSharedVariableIsRejected()
    {
        ColumnarTripleIndex index = Fixture();
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        TriplePattern left = new(PatternPosition.OfVariable(a), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(b));
        TriplePattern right = new(PatternPosition.OfVariable(c), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(d));

        IReadOnlyList<Variable> leftSchema = ColumnarBatchScan.ScanSchemaOf(index, left);
        IReadOnlyList<Variable> rightSchema = ColumnarBatchScan.ScanSchemaOf(index, right);
        List<SolutionBatch> leftRelation = Materialise(index, left);
        List<SolutionBatch> rightRelation = Materialise(index, right);

        Assert.ThrowsExactly<ArgumentException>(() => SolutionBatchSemijoin.Reduce(leftRelation, leftSchema, rightRelation, rightSchema));
    }
}
