using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// An open-addressed variant of <see cref="SolutionBatchHashTable"/>: the
/// build side of a batched hash join with the same chained row storage, but
/// the key→chain-head map is an <see cref="OpenAddressedTable{TValue}"/> rather
/// than a <see cref="Dictionary{TKey,TValue}"/>. The flat, linear-probed map
/// keeps a lookup cache-local with no per-entry node indirection — the
/// constant-factor the swap targets. <see cref="SolutionBatchHashTable"/> is
/// the differential oracle: both answer <see cref="FirstMatch"/>/
/// <see cref="NextMatch"/> identically over any build set, so one can stand in
/// for the other behind the join.
/// </summary>
/// <remarks>
/// Same contract as the chained table: built once by <see cref="Build"/>, then
/// read-only through <see cref="FirstMatch"/>/<see cref="NextMatch"/>/
/// <see cref="ValueAt"/>. Single-threaded by construction. Keys pack one or two
/// join values into a <see cref="JoinKey"/>, exactly as the chained table. The
/// open-addressing mechanics live in <see cref="OpenAddressedTable{TValue}"/>;
/// this type only owns the chained row storage over it.
/// </remarks>
[DebuggerDisplay("OpenAddressedBatchHashTable Rows={RowCount} Columns={ColumnCount} Keys={head.Count} Slots={head.Capacity}")]
public sealed class OpenAddressedBatchHashTable
{
    /// <summary>The sentinel a probe returns when no build row carries the key.</summary>
    private const int NoMatch = -1;

    private readonly List<uint>[] columns;

    private readonly List<int> next;

    //Key -> chain-head row id. The head stores the most-recently-inserted row
    //for a key; `next` links each earlier row carrying the same key.
    private readonly OpenAddressedTable<int> head;

    /// <summary>The number of build columns, positional against the build schema.</summary>
    public int ColumnCount => columns.Length;

    /// <summary>The number of materialised build rows.</summary>
    public int RowCount => next.Count;

    /// <summary>The open-addressed map's slot count — its key/value/control arrays each hold this many entries.</summary>
    public int SlotCount => head.Capacity;

    /// <summary>Constructs an empty table over the given column count.</summary>
    /// <param name="columnCount">The build schema's column count.</param>
    private OpenAddressedBatchHashTable(int columnCount)
    {
        columns = new List<uint>[columnCount];
        for(int i = 0; i < columnCount; i++)
        {
            columns[i] = [];
        }

        next = [];
        head = new OpenAddressedTable<int>();
    }

    /// <summary>
    /// Builds the table by draining <paramref name="buildBatches"/>: every
    /// row's columns append contiguously and its packed key joins the chain
    /// through the open-addressed map.
    /// </summary>
    /// <param name="buildBatches">The build side's batch stream; consumed eagerly.</param>
    /// <param name="columnCount">The build schema's column count.</param>
    /// <param name="keyColumn0">The first key column's index in the build schema.</param>
    /// <param name="keyColumn1">The second key column's index, or −1 for a single-variable key.</param>
    /// <returns>The built table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="buildBatches"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columnCount"/> is negative.</exception>
    public static OpenAddressedBatchHashTable Build(
        IEnumerable<SolutionBatch> buildBatches,
        int columnCount,
        int keyColumn0,
        int keyColumn1)
    {
        ArgumentNullException.ThrowIfNull(buildBatches);
        ArgumentOutOfRangeException.ThrowIfNegative(columnCount);

        OpenAddressedBatchHashTable table = new(columnCount);

        foreach(SolutionBatch batch in buildBatches)
        {
            int count = batch.Count;
            ReadOnlySpan<uint> key0Column = batch.ColumnOf(keyColumn0);
            ReadOnlySpan<uint> key1Column = keyColumn1 >= 0 ? batch.ColumnOf(keyColumn1) : default;

            for(int row = 0; row < count; row++)
            {
                int rowId = table.next.Count;
                for(int column = 0; column < columnCount; column++)
                {
                    table.columns[column].Add(batch.ColumnOf(column)[row]);
                }

                JoinKey key = JoinKey.Pack(key0Column[row], keyColumn1 >= 0 ? key1Column[row] : 0);

                //Place this row as the new chain head; the prior head (or −1
                //for a new key) becomes its successor in the chain.
                table.next.Add(table.head.Exchange(key.Value, rowId, out int previous) ? previous : NoMatch);
            }
        }

        return table;
    }

    /// <summary>Returns the first matching build row for a packed key, or −1 when none match.</summary>
    /// <param name="key">The packed probe key.</param>
    /// <returns>The first matching build row id, or −1.</returns>
    public int FirstMatch(JoinKey key)
    {
        return head.TryGetValue(key.Value, out int rowId) ? rowId : NoMatch;
    }

    /// <summary>Returns the next build row sharing the current row's key, or −1 at the chain's end.</summary>
    /// <param name="rowId">A build row id from <see cref="FirstMatch"/> or a prior <see cref="NextMatch"/>.</param>
    /// <returns>The next matching build row id, or −1.</returns>
    public int NextMatch(int rowId)
    {
        return next[rowId];
    }

    /// <summary>Reads a materialised build value.</summary>
    /// <param name="column">The build column index.</param>
    /// <param name="rowId">The build row id.</param>
    /// <returns>The value.</returns>
    public uint ValueAt(int column, int rowId)
    {
        return columns[column][rowId];
    }
}
