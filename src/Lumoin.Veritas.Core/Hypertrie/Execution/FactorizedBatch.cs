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
    /// prefixed with the group key; batches are full except the last.
    /// </summary>
    /// <returns>The flattened batch stream.</returns>
    public IEnumerable<SolutionBatch> Flatten()
    {
        SolutionBatch output = new(Schema);
        int rows = 0;

        foreach(FactorizedGroup group in Groups)
        {
            int branchCount = BranchColumns.Length;

            //A mixed-radix odometer over the branch row indices walks the
            //cross product without recursion: the least-significant branch
            //advances first and carries into the next when it wraps.
            int[] cursor = new int[branchCount];
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
                    int stride = branchColumns.Length;
                    int rowOffset = cursor[branch] * stride;
                    uint[] values = group.BranchValues[branch];

                    for(int column = 0; column < stride; column++)
                    {
                        output.ColumnSpan(branchColumns[column])[rows] = values[rowOffset + column];
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

                Advance(cursor, group.BranchRowCounts);
            }
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
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
