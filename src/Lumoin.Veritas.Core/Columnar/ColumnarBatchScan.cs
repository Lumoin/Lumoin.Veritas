using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Streams one triple pattern's matches as
/// <see cref="SolutionBatch"/>es — the batched execution spine's
/// source operator, and the bandwidth path the per-row drivers
/// cannot reach: a delta-free index is walked level by level
/// through block-decoding readers, filling whole columns instead
/// of materialising per-row solutions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Schema.</b> The batch schema is the pattern's variables in
/// the SELECTED permutation's tail order (the scan picks any
/// materialised permutation whose prefix covers the bound
/// positions); <see cref="ScanSchemaOf"/> reports it so consumers
/// wire columns before scanning.
/// </para>
/// <para>
/// <b>Delta.</b> An index carrying an accumulated delta falls back
/// to the merged per-triple enumeration, still batching the
/// output — correctness is identical, only the fill path differs.
/// Compaction keeps the delta-free fast path the common case.
/// </para>
/// </remarks>
public static class ColumnarBatchScan
{
    /// <summary>
    /// The batch schema <see cref="Scan"/> will produce for the
    /// pattern on the index: the pattern's variables in the
    /// selected permutation's tail order.
    /// </summary>
    /// <param name="index">The index to scan.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The schema.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The pattern is a per-pattern self-join, which the scan does not evaluate.</exception>
    public static IReadOnlyList<Variable> ScanSchemaOf(ColumnarTripleIndex index, TriplePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(index);

        (int permutationIndex, _) = SelectPermutation(index, pattern);
        ReadOnlySpan<byte> permutation = ColumnarTripleIndex.PermutationAt(permutationIndex);

        List<Variable> schema = [];
        for(int level = 0; level < 3; level++)
        {
            if(pattern.At(permutation[level]).IsVariable)
            {
                schema.Add(pattern.At(permutation[level]).Variable);
            }
        }

        return schema;
    }

    /// <summary>
    /// Streams the pattern's matches as batches over the schema
    /// <see cref="ScanSchemaOf"/> reports.
    /// </summary>
    /// <param name="index">The index to scan.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The batch stream; batches are yielded full except the last.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The pattern is a per-pattern self-join.</exception>
    public static IEnumerable<SolutionBatch> Scan(ColumnarTripleIndex index, TriplePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(index);

        IReadOnlyList<Variable> schema = ScanSchemaOf(index, pattern);

        return index.HasDelta
            ? ScanMerged(index, pattern, schema)
            : ScanBase(index, pattern, schema);
    }

    /// <summary>Selects a materialised permutation whose prefix covers the pattern's bound positions, preferring the lowest index for determinism.</summary>
    /// <param name="index">The index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The permutation index and the bound-position count.</returns>
    private static (int PermutationIndex, int BoundCount) SelectPermutation(ColumnarTripleIndex index, TriplePattern pattern)
    {
        if(pattern.HasSelfJoin())
        {
            throw new ArgumentException("Per-pattern self-joins are not scannable; route through the join drivers.", nameof(pattern));
        }

        Span<byte> boundPositions = stackalloc byte[3];
        int boundCount = 0;
        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            if(pattern.At(rdfPosition).IsBound)
            {
                boundPositions[boundCount++] = (byte)rdfPosition;
            }
        }

        for(int i = 0; i < 6; i++)
        {
            if(!index.IsPermutationAvailable(i))
            {
                continue;
            }

            ReadOnlySpan<byte> permutation = ColumnarTripleIndex.PermutationAt(i);
            bool covers = true;
            for(int j = 0; j < boundCount && covers; j++)
            {
                covers = boundPositions[..boundCount].IndexOf(permutation[j]) >= 0;
            }

            if(covers)
            {
                return (i, boundCount);
            }
        }

        //Three rotations cover every bound set and all-six covers
        //everything; reaching here means no order was materialised
        //at all.
        throw new InvalidOperationException("No materialised permutation covers the pattern's bound positions.");
    }

    /// <summary>The delta-free fast path: walk the CSR levels through block-decoding readers, filling whole columns.</summary>
    /// <param name="index">The delta-free index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="schema">The scan schema.</param>
    /// <returns>The batch stream.</returns>
    private static IEnumerable<SolutionBatch> ScanBase(ColumnarTripleIndex index, TriplePattern pattern, IReadOnlyList<Variable> schema)
    {
        (int permutationIndex, int boundCount) = SelectPermutation(index, pattern);
        ReadOnlySpan<byte> permutation = ColumnarTripleIndex.PermutationAt(permutationIndex);
        ColumnarOrder order = index.OrderAt(permutationIndex);

        BlockPackedColumnReader values0 = new(order.ValuesColumnAt(0));
        BlockPackedColumnReader offsets0 = new(order.OffsetsColumnAt(0));
        BlockPackedColumnReader values1 = new(order.ValuesColumnAt(1));
        BlockPackedColumnReader offsets1 = new(order.OffsetsColumnAt(1));
        BlockPackedColumnReader values2 = new(order.ValuesColumnAt(2));

        //Descend the bound prefix; a missing constant means no
        //matches at all.
        (int lo, int hi) = index.Level0BoundsAt(permutationIndex);
        int level = 0;
        for(; level < boundCount; level++)
        {
            uint boundValue = pattern.At(permutation[level]).BoundTerm.Encoded;
            BlockPackedColumnReader values = level switch { 0 => values0, 1 => values1, _ => values2 };
            int found = values.LowerBound(lo, hi, boundValue);

            if(found >= hi || values.ValueAt(found) != boundValue)
            {
                yield break;
            }

            if(level == 2)
            {
                break;
            }

            BlockPackedColumnReader offsets = level == 0 ? offsets0 : offsets1;
            lo = (int)offsets.ValueAt(found);
            hi = (int)offsets.ValueAt(found + 1);
        }

        //Fully bound: the prefix descent IS the membership test.
        if(boundCount == 3)
        {
            SolutionBatch single = new(schema);
            single.SetCount(1);

            yield return single;
            yield break;
        }

        //The variable levels fill columns. The loops are written per
        //bound count so each shape stays a straight nested walk.
        SolutionBatch batch = new(schema);
        int rows = 0;

        if(boundCount == 2)
        {
            //One variable level: [lo, hi) of the deepest level.
            for(int k = lo; k < hi; k++)
            {
                batch.ColumnSpan(0)[rows] = values2.ValueAt(k);
                rows++;

                if(rows == SolutionBatch.BatchLength)
                {
                    batch.SetCount(rows);

                    yield return batch;

                    batch = new SolutionBatch(schema);
                    rows = 0;
                }
            }
        }
        else if(boundCount == 1)
        {
            for(int j = lo; j < hi; j++)
            {
                uint middle = values1.ValueAt(j);
                int deepLo = (int)offsets1.ValueAt(j);
                int deepHi = (int)offsets1.ValueAt(j + 1);

                for(int k = deepLo; k < deepHi; k++)
                {
                    batch.ColumnSpan(0)[rows] = middle;
                    batch.ColumnSpan(1)[rows] = values2.ValueAt(k);
                    rows++;

                    if(rows == SolutionBatch.BatchLength)
                    {
                        batch.SetCount(rows);

                        yield return batch;

                        batch = new SolutionBatch(schema);
                        rows = 0;
                    }
                }
            }
        }
        else
        {
            for(int i = lo; i < hi; i++)
            {
                uint top = values0.ValueAt(i);
                int midLo = (int)offsets0.ValueAt(i);
                int midHi = (int)offsets0.ValueAt(i + 1);

                for(int j = midLo; j < midHi; j++)
                {
                    uint middle = values1.ValueAt(j);
                    int deepLo = (int)offsets1.ValueAt(j);
                    int deepHi = (int)offsets1.ValueAt(j + 1);

                    for(int k = deepLo; k < deepHi; k++)
                    {
                        batch.ColumnSpan(0)[rows] = top;
                        batch.ColumnSpan(1)[rows] = middle;
                        batch.ColumnSpan(2)[rows] = values2.ValueAt(k);
                        rows++;

                        if(rows == SolutionBatch.BatchLength)
                        {
                            batch.SetCount(rows);

                            yield return batch;

                            batch = new SolutionBatch(schema);
                            rows = 0;
                        }
                    }
                }
            }
        }

        if(rows > 0)
        {
            batch.SetCount(rows);

            yield return batch;
        }
    }

    /// <summary>The delta fallback: the merged per-triple enumeration filtered by the bound positions, batching the output.</summary>
    /// <param name="index">The delta-carrying index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="schema">The scan schema.</param>
    /// <returns>The batch stream.</returns>
    private static IEnumerable<SolutionBatch> ScanMerged(ColumnarTripleIndex index, TriplePattern pattern, IReadOnlyList<Variable> schema)
    {
        (int permutationIndex, _) = SelectPermutation(index, pattern);
        ReadOnlySpan<byte> permutation = ColumnarTripleIndex.PermutationAt(permutationIndex);

        //Map each schema column to the RDF position it reads.
        Span<byte> columnPositions = stackalloc byte[3];
        int columnCount = 0;
        for(int level = 0; level < 3; level++)
        {
            if(pattern.At(permutation[level]).IsVariable)
            {
                columnPositions[columnCount++] = permutation[level];
            }
        }

        byte position0 = columnCount > 0 ? columnPositions[0] : (byte)0;
        byte position1 = columnCount > 1 ? columnPositions[1] : (byte)0;
        byte position2 = columnCount > 2 ? columnPositions[2] : (byte)0;

        return ScanMergedCore(index, pattern, schema, columnCount, position0, position1, position2);
    }

    /// <summary>The iterator body behind <see cref="ScanMerged"/>; split out because iterators cannot take spans.</summary>
    /// <param name="index">The delta-carrying index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="schema">The scan schema.</param>
    /// <param name="columnCount">The variable column count.</param>
    /// <param name="position0">The first column's RDF position.</param>
    /// <param name="position1">The second column's RDF position, when present.</param>
    /// <param name="position2">The third column's RDF position, when present.</param>
    /// <returns>The batch stream.</returns>
    private static IEnumerable<SolutionBatch> ScanMergedCore(
        ColumnarTripleIndex index,
        TriplePattern pattern,
        IReadOnlyList<Variable> schema,
        int columnCount,
        byte position0,
        byte position1,
        byte position2)
    {
        SolutionBatch batch = new(schema);
        int rows = 0;

        foreach(EncodedTriple triple in index.EnumerateTriples())
        {
            if(!Matches(pattern, triple))
            {
                continue;
            }

            if(columnCount > 0)
            {
                batch.ColumnSpan(0)[rows] = ColumnarSearch.ColumnAt(in triple, position0);
            }

            if(columnCount > 1)
            {
                batch.ColumnSpan(1)[rows] = ColumnarSearch.ColumnAt(in triple, position1);
            }

            if(columnCount > 2)
            {
                batch.ColumnSpan(2)[rows] = ColumnarSearch.ColumnAt(in triple, position2);
            }

            rows++;

            if(rows == SolutionBatch.BatchLength)
            {
                batch.SetCount(rows);

                yield return batch;

                batch = new SolutionBatch(schema);
                rows = 0;
            }
        }

        if(rows > 0)
        {
            batch.SetCount(rows);

            yield return batch;
        }
    }

    /// <summary>Whether a triple matches the pattern's bound positions.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="triple">The candidate triple.</param>
    /// <returns><c>true</c> on a match.</returns>
    private static bool Matches(TriplePattern pattern, EncodedTriple triple)
    {
        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            PatternPosition slot = pattern.At(rdfPosition);

            if(slot.IsBound && slot.BoundTerm.Encoded != ColumnarSearch.ColumnAt(in triple, (byte)rdfPosition))
            {
                return false;
            }
        }

        return true;
    }
}
