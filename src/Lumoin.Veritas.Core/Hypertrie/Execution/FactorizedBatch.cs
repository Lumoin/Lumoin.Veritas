using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// A factorised intermediate result: a union of <see cref="FactorizedGroup"/>
/// product nodes over a shared <see cref="Schema"/>. Where a
/// <see cref="SolutionBatch"/> stores the flat cross product of a join row
/// by row, this stores it as a product of unions — for each key tuple the
/// branches that vary independently given that key are kept apart, so the
/// representation pays the SUM of the branch sizes for what flattens to
/// their PRODUCT of rows. On a fan-out join (many build matches AND many
/// probe matches per key) the factorised form is strictly smaller; on a
/// one-to-one shape it is the same size, never larger.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> <see cref="KeyColumns"/> and <see cref="BranchColumns"/>
/// are schema-column indices that together partition every column of
/// <see cref="Schema"/>: the key columns carry the value shared across a
/// group, each branch's columns carry one independently-varying tuple. A
/// group's stored data is positional against this partition.
/// </para>
/// <para>
/// <b>Bridge.</b> <see cref="Flatten"/> expands the groups back into the
/// row-major <see cref="SolutionBatch"/> stream the flat operators consume —
/// the differential oracle that proves a factorised producer answer-identical
/// to its flat counterpart. The factorisation's win lives BEFORE this point;
/// flattening reconstructs the product it compressed.
/// </para>
/// </remarks>
[DebuggerDisplay("FactorizedBatch Variables={Schema.Count} Groups={Groups.Count} Flat={FlatRowCount}")]
public sealed class FactorizedBatch
{
    /// <summary>The variables every group binds, positionally — the flattened row schema.</summary>
    public IReadOnlyList<Variable> Schema { get; }

    /// <summary>The schema-column indices of the key tuple shared within each group, one or two.</summary>
    public int[] KeyColumns { get; }

    /// <summary>The schema-column indices of each branch's columns; the branches and the key columns together partition the schema.</summary>
    public int[][] BranchColumns { get; }

    /// <summary>The product nodes; their flattened rows, unioned, are this batch's result.</summary>
    public IReadOnlyList<FactorizedGroup> Groups { get; }

    /// <summary>Whether any group carries a nested branch — the second factorisation level, present for a chain and absent for the single-level star. Decided once at construction so <see cref="Flatten"/> takes the flat path with no per-call scan when there is no nesting.</summary>
    public bool HasNestedBranches { get; }

    /// <summary>Constructs a factorised batch over a schema partitioned into key columns and branch column groups.</summary>
    /// <param name="schema">The flattened row schema; shared by every group.</param>
    /// <param name="keyColumns">The schema-column indices of the per-group key tuple.</param>
    /// <param name="branchColumns">The schema-column indices of each branch's columns.</param>
    /// <param name="groups">The product nodes.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public FactorizedBatch(IReadOnlyList<Variable> schema, int[] keyColumns, int[][] branchColumns, IReadOnlyList<FactorizedGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(keyColumns);
        ArgumentNullException.ThrowIfNull(branchColumns);
        ArgumentNullException.ThrowIfNull(groups);

        Schema = schema;
        KeyColumns = keyColumns;
        BranchColumns = branchColumns;
        Groups = groups;

        bool nested = false;
        foreach(FactorizedGroup group in groups)
        {
            if(group.NestedBranches is not null)
            {
                nested = true;

                break;
            }
        }

        HasNestedBranches = nested;
    }

    /// <summary>The number of flat rows this batch stands for: the sum over groups of each group's product of branch sizes.</summary>
    public long FlatRowCount
    {
        get
        {
            long total = 0;
            foreach(FactorizedGroup group in Groups)
            {
                total += group.FlatRowCount;
            }

            return total;
        }
    }

    /// <summary>The number of value tuples actually stored: the sum over groups of each group's branch sizes — the factorisation's footprint against <see cref="FlatRowCount"/>.</summary>
    public long FactorizedTupleCount
    {
        get
        {
            long total = 0;
            foreach(FactorizedGroup group in Groups)
            {
                total += group.FactorizedTupleCount;
            }

            return total;
        }
    }

    /// <summary>
    /// Expands the groups into the row-major <see cref="SolutionBatch"/>
    /// stream over <see cref="Schema"/> — the flat result the factorisation
    /// compressed. Each group emits the cross product of its branches,
    /// prefixed with the group key; batches are full except the last. The
    /// flat path runs directly off the packed branches; a batch with nested
    /// branches takes the path that materialises each nested sub-batch's rows
    /// before the cross product.
    /// </summary>
    /// <returns>The flattened batch stream.</returns>
    public IEnumerable<SolutionBatch> Flatten()
    {
        return HasNestedBranches ? FlattenNested() : FlattenFlat();
    }

    /// <summary>The flatten path for an all-flat batch: a mixed-radix odometer reads the packed branch tuples directly into the output rows.</summary>
    /// <returns>The flattened batch stream.</returns>
    private IEnumerable<SolutionBatch> FlattenFlat()
    {
        SolutionBatch output = new(Schema);
        int rows = 0;
        int branchCount = BranchColumns.Length;

        foreach(FactorizedGroup group in Groups)
        {
            //A mixed-radix odometer over the branch row indices walks the
            //cross product without recursion: the least-significant branch
            //advances first and carries into the next when it wraps. The radix
            //is each branch's row count.
            int[] cursor = new int[branchCount];
            int[] radix = new int[branchCount];
            for(int branch = 0; branch < branchCount; branch++)
            {
                radix[branch] = group.Branches.RowCountOf(branch);
            }

            long combinations = group.FlatRowCount;

            for(long combination = 0; combination < combinations; combination++)
            {
                for(int k = 0; k < KeyColumns.Length; k++)
                {
                    output.ColumnSpan(KeyColumns[k])[rows] = group.KeyValues[k];
                }

                for(int branch = 0; branch < branchCount; branch++)
                {
                    int[] branchColumns = BranchColumns[branch];
                    for(int column = 0; column < branchColumns.Length; column++)
                    {
                        output.ColumnSpan(branchColumns[column])[rows] = group.Branches.ValueAt(branch, cursor[branch], column);
                    }
                }

                rows++;

                if(rows == SolutionBatch.BatchLength)
                {
                    output.SetCount(rows);

                    yield return output;

                    output = new SolutionBatch(Schema);
                    rows = 0;
                }

                Advance(cursor, radix);
            }
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
    }

    /// <summary>
    /// The flatten path for a batch with nested branches: a flat branch is read
    /// in place through the group's packed branches, a nested branch's sub-rows
    /// are first materialised row-major into that branch slot's reusable flat
    /// buffer (grown on demand, reused across groups), and the odometer then
    /// walks the cross product over both. Peak memory is the sum of the branch
    /// sub-row sizes, never their product, and no per-row tuple is allocated.
    /// </summary>
    /// <returns>The flattened batch stream.</returns>
    private IEnumerable<SolutionBatch> FlattenNested()
    {
        SolutionBatch output = new(Schema);
        int rows = 0;
        int branchCount = BranchColumns.Length;

        uint[][] nestedRows = new uint[branchCount][];
        for(int branch = 0; branch < branchCount; branch++)
        {
            nestedRows[branch] = [];
        }

        bool[] isNested = new bool[branchCount];
        int[] counts = new int[branchCount];
        int[] cursor = new int[branchCount];

        foreach(FactorizedGroup group in Groups)
        {
            long combinations = 1;
            for(int branch = 0; branch < branchCount; branch++)
            {
                FactorizedBatch? nested = group.NestedAt(branch);
                isNested[branch] = nested is not null;
                counts[branch] = nested is null
                    ? group.Branches.RowCountOf(branch)
                    : MaterializeNested(nested, branch, nestedRows);
                combinations *= counts[branch];
            }

            Array.Clear(cursor);
            for(long combination = 0; combination < combinations; combination++)
            {
                for(int k = 0; k < KeyColumns.Length; k++)
                {
                    output.ColumnSpan(KeyColumns[k])[rows] = group.KeyValues[k];
                }

                for(int branch = 0; branch < branchCount; branch++)
                {
                    int[] branchColumns = BranchColumns[branch];
                    if(isNested[branch])
                    {
                        uint[] sub = nestedRows[branch];
                        int offset = cursor[branch] * branchColumns.Length;
                        for(int column = 0; column < branchColumns.Length; column++)
                        {
                            output.ColumnSpan(branchColumns[column])[rows] = sub[offset + column];
                        }
                    }
                    else
                    {
                        for(int column = 0; column < branchColumns.Length; column++)
                        {
                            output.ColumnSpan(branchColumns[column])[rows] = group.Branches.ValueAt(branch, cursor[branch], column);
                        }
                    }
                }

                rows++;

                if(rows == SolutionBatch.BatchLength)
                {
                    output.SetCount(rows);

                    yield return output;

                    output = new SolutionBatch(Schema);
                    rows = 0;
                }

                Advance(cursor, counts);
            }
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
    }

    /// <summary>
    /// Materialises a single-level nested sub-batch row-major into its branch
    /// slot's reusable buffer, growing the buffer when the sub-rows outgrow it.
    /// The sub-batch carries no further nesting (the depth-2 guarantee), so the
    /// flat path expands it without recursion; the row width is the sub-batch's
    /// schema width, which equals the parent branch's column count.
    /// </summary>
    /// <param name="nested">The single-level nested sub-batch.</param>
    /// <param name="branch">The branch slot the sub-rows are materialised into.</param>
    /// <param name="nestedRows">The per-branch reusable flat buffers.</param>
    /// <returns>The materialised sub-row count.</returns>
    private static int MaterializeNested(FactorizedBatch nested, int branch, uint[][] nestedRows)
    {
        int width = nested.Schema.Count;
        int count = checked((int)nested.FlatRowCount);
        long needed = (long)count * width;
        uint[] buffer = nestedRows[branch];
        if(buffer.Length < needed)
        {
            buffer = new uint[Math.Max(needed, (long)buffer.Length * 2)];
            nestedRows[branch] = buffer;
        }

        int next = 0;
        foreach(SolutionBatch batch in nested.FlattenFlat())
        {
            for(int row = 0; row < batch.Count; row++)
            {
                for(int column = 0; column < width; column++)
                {
                    buffer[next] = batch.ColumnOf(column)[row];
                    next++;
                }
            }
        }

        return count;
    }

    /// <summary>Advances the mixed-radix odometer one step: increment the last branch, carrying into earlier branches as each wraps past its row count.</summary>
    /// <param name="cursor">The per-branch row cursor, advanced in place.</param>
    /// <param name="rowCounts">Each branch's row count — the radix of that digit.</param>
    private static void Advance(int[] cursor, int[] rowCounts)
    {
        for(int branch = cursor.Length - 1; branch >= 0; branch--)
        {
            cursor[branch]++;
            if(cursor[branch] < rowCounts[branch])
            {
                return;
            }

            cursor[branch] = 0;
        }
    }
}
